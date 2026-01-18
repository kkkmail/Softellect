# VPN Server Linux Migration Analysis Report

## Executive Summary

This report analyzes the feasibility of running the VPN server on Linux (AlmaLinux 9) using .NET 10, starting from the existing Windows service-based server. The analysis confirms that **Linux server migration is feasible with minimal structural changes**, following a pattern similar to the existing Android client adaptation.

**Key Findings:**
- The server has **two primary Windows-only blockers**: WinTun adapter usage and Windows Service hosting
- The existing Android client pattern (linked files + conditional compilation) can be directly applied
- A **minimal Interop split** is required to isolate Windows-only TUN adapter code from shared types
- The Wcf library requires **conditional compilation** for the `.UseWindowsService()` call
- Server files under `Vpn\Server\` that use WinTun (PacketRouter.fs, ExternalInterface.fs) need **Linux-specific alternatives**

---

## 1. Dependency Walk

### 1.1 Dependency Chain (Textual Diagram)

```
Apps\Vpn\VpnServer\Program.fs (entrypoint)
    |
    +-- Vpn\Server\Server.fsproj (library)
    |       |
    |       +-- Vpn\Server\Program.fs         --> vpnServerMain
    |       +-- Vpn\Server\WcfServer.fs       --> CoreWCF hosting
    |       +-- Vpn\Server\Service.fs         --> IHostedService
    |       +-- Vpn\Server\PacketRouter.fs    --> **WinTun usage** (WINDOWS-ONLY)
    |       +-- Vpn\Server\ExternalInterface.fs --> Raw sockets (IOControl SIO_RCVALL)
    |       +-- Vpn\Server\DnsProxy.fs        --> Pure .NET (cross-platform)
    |       +-- Vpn\Server\IcmpProxy.fs       --> Pure .NET (cross-platform)
    |       +-- Vpn\Server\Nat.fs             --> Pure .NET (cross-platform)
    |       +-- Vpn\Server\ClientRegistry.fs  --> Pure .NET (cross-platform)
    |       +-- Vpn\Server\UdpServer.fs       --> Pure .NET (cross-platform)
    |       |
    |       +-- Vpn\Interop\Interop.csproj    --> **WINDOWS-ONLY**
    |       |       |
    |       |       +-- WinTun.cs             --> wintun.dll P/Invoke
    |       |       +-- WinTunAdapter.cs      --> netsh commands, WinTun wrapper
    |       |       +-- KillSwitch.cs         --> WFP (fwpuclnt.dll) for kill-switch
    |       |       +-- WindowsFilteringPlatform.cs --> WFP P/Invoke
    |       |
    |       +-- Vpn\Core\Core.fsproj          --> Cross-platform (no Interop dep)
    |       +-- Wcf\Wcf.fsproj                --> CoreWCF + **UseWindowsService()**
    |       +-- Sys\Sys.fsproj                --> Mixed (WindowsApi.fs, ServiceInstaller.fs)
    |
    +-- Apps\Vpn\VpnServerAdm\VpnServerAdm.fsproj (admin utility, client-side)
```

### 1.2 Key Hosting Configuration Locations

| Concern | File | Line | Notes |
|---------|------|------|-------|
| Windows Service | `Wcf\Program.fs` | 44 | `.UseWindowsService()` |
| Windows Service (ref) | `Vpn\Server\Server.fsproj` | 38 | `Microsoft.Extensions.Hosting.WindowsServices` |
| Windows Service (ref) | `Apps\Vpn\VpnServer\VpnServer.fsproj` | 71 | Same package |
| CoreWCF wiring | `Wcf\Program.fs` | 38-77 | `createHostBuilder` function |
| Service entrypoint | `Vpn\Server\Program.fs` | 59 | `vpnServerMain` |

---

## 2. Windows Dependency Classification

### 2.1 Windows Service Hosting

| File(s) | Project | Scope | Description |
|---------|---------|-------|-------------|
| `Wcf\Program.fs:44` | Wcf | Server + Client | `.UseWindowsService()` call |
| `Microsoft.Extensions.Hosting.WindowsServices` | Wcf, Server, VpnServer | Server + Client | NuGet package reference |

**Impact:** Prevents compilation on Linux due to Windows-only hosting APIs.

**Mitigation:** Conditional compilation or platform-specific project reference.

### 2.2 Native Windows Interop (Wintun)

| File(s) | Project | Scope | Description |
|---------|---------|-------|-------------|
| `Vpn\Interop\WinTun.cs` | Interop | Client + Server | P/Invoke to wintun.dll |
| `Vpn\Interop\WinTunAdapter.cs` | Interop | Client + Server | Managed wrapper, uses netsh |
| `Vpn\Native\wintun.dll` | Interop | Client + Server | Native Windows DLL |
| `Vpn\Server\PacketRouter.fs:12,153` | Server | Server-only | Uses `WinTunAdapter` |
| `Vpn\Client\Tunnel.fs:11,46` | Client | Client-only | Uses `WinTunAdapter` |
| `Vpn\Client\Service.fs:17` | Client | Client-only | Uses Interop |

**Impact:** The entire Interop project is Windows-only. Both server and client use it.

**Mitigation:** Split Interop into Common/Windows parts; Linux server will not use TUN adapter (uses raw sockets directly).

### 2.3 Windows Filtering Platform (Kill-Switch)

| File(s) | Project | Scope | Description |
|---------|---------|-------|-------------|
| `Vpn\Interop\KillSwitch.cs` | Interop | Client-only | WFP-based kill-switch |
| `Vpn\Interop\WindowsFilteringPlatform.cs` | Interop | Client-only | fwpuclnt.dll P/Invoke |

**Impact:** Client-only feature; does not affect server.

**Mitigation:** Remains in Windows-only Interop; Linux server does not need this.

### 2.4 Windows-Only Networking

| File(s) | Project | Scope | Description |
|---------|---------|-------|-------------|
| `Vpn\Server\ExternalInterface.fs:200` | Server | Server-only | `IOControl(IOControlCode.ReceiveAll)` for raw socket |

**Impact:** Windows-specific raw socket mode (SIO_RCVALL). Linux raw sockets do not require this.

**Mitigation:** Conditional compilation or platform-specific file.

### 2.5 Filesystem / Identity / EventLog / Registry

| File(s) | Project | Scope | Description |
|---------|---------|-------|-------------|
| `Sys\WindowsApi.fs` | Sys | Server + Client | Monitor resolution, DPI (user32.dll, gdi32.dll) |
| `Sys\ServiceInstaller.fs` | Sys | Server + Client | Windows service installation (`ServiceController`) |

**Impact:** These files are not directly used by server packet processing but are referenced by Sys.

**Mitigation:** Already handled by Android pattern (Sys files are linked selectively).

---

## 3. Android Comparison

### 3.1 How Platform Separation is Handled

The Android client uses a **separate solution** (`SoftellectAndroid.slnx`) with platform-specific projects:

```
Android\
    Sys\Sys.fsproj          --> net10.0-android, ANDROID symbol
    Wcf\Wcf.fsproj          --> net10.0-android, ANDROID symbol
    Core\Core.fsproj        --> net10.0-android, ANDROID symbol

Vpn\
    AndroidClient\AndroidClient.fsproj  --> net10.0-android
```

**Key patterns observed:**

1. **Linked files**: Android projects link source files from Windows counterparts:
   ```xml
   <Compile Include="..\..\Sys\Primitives.fs" Link="Primitives.fs" />
   ```

2. **Conditional compilation symbol**: `ANDROID` defined in project properties:
   ```xml
   <DefineConstants>$(DefineConstants);ANDROID</DefineConstants>
   ```

3. **Platform-specific files**: Android has its own `Logging.fs` instead of linking the Windows version (which uses log4net).

4. **Selective dependencies**: Android Wcf only includes client-side files:
   ```xml
   <Compile Include="..\..\Wcf\Errors.fs" Link="Errors.fs" />
   <Compile Include="..\..\Wcf\Common.fs" Link="Common.fs" />
   <Compile Include="..\..\Wcf\Client.fs" Link="Client.fs" />
   <!-- No Service.fs, Program.fs - server-side not needed -->
   ```

5. **No Interop reference**: Android client does not reference the Windows Interop project.

### 3.2 Applicability to Linux Server

> **Can the same pattern be applied to a Linux server with minimal disruption?**

**Yes, with caveats:**

| Aspect | Android Client | Linux Server |
|--------|---------------|--------------|
| Solution | Separate (`SoftellectAndroid.slnx`) | **Could be main solution** |
| Target framework | `net10.0-android` | `net10.0` (same as Windows) |
| Conditional symbol | `ANDROID` | `LINUX` |
| Interop dependency | None (Android uses OS VPN API) | Partial (needs types, not WinTun) |
| Platform-specific code | `VpnTunnelService.fs`, `Logging.fs` | `PacketRouter.fs`, `ExternalInterface.fs` |

**Key difference:** The Android client is a **GUI application** using Android's VpnService API. The Linux server is a **console/daemon** that needs raw socket access - which works cross-platform in .NET without WinTun.

---

## 4. Linux Server Project Proposal

### 4.1 Recommendation: Add to Main Solution

**Recommendation:** Add the Linux server project to the **main solution** (`SoftellectMain.slnx`), unlike Android which has a separate solution.

**Justification:**

| Factor | Main Solution | Separate Solution |
|--------|---------------|-------------------|
| Build impact | Requires conditional build | Isolated build |
| Conceptual clarity | Server variants together | Platform separation |
| Maintenance cost | Single solution to maintain | Two solutions to sync |
| CI/CD complexity | One pipeline with conditions | Separate pipelines |

**Reasoning:**

1. **Target framework compatibility:** Unlike Android (`net10.0-android`), Linux server targets plain `net10.0` - the same as Windows server. This means both can coexist in the same solution without TFM conflicts.

2. **Shared code maximization:** Server-side code (WcfServer.fs, Service.fs, DnsProxy.fs, IcmpProxy.fs, Nat.fs, etc.) is largely cross-platform. Only PacketRouter.fs and ExternalInterface.fs need platform variants.

3. **Build configuration:** Use `<Condition>` in project files or solution configurations to exclude Linux project on Windows builds:
   ```xml
   <PropertyGroup Condition="'$(RuntimeIdentifier)'=='linux-x64'">
     <DefineConstants>$(DefineConstants);LINUX</DefineConstants>
   </PropertyGroup>
   ```

4. **Android precedent:** Android is separate because it targets a completely different runtime (Xamarin/MAUI) with incompatible project structure. Linux server does not have this constraint.

---

## 5. Interop Split Recommendation

### 5.1 Proposed Structure

```
Vpn\
    Interop\                          --> KEEP (Windows-only, renamed conceptually)
        Interop.csproj                --> Windows TUN adapter + KillSwitch
        WinTun.cs                     --> Unchanged
        WinTunAdapter.cs              --> Unchanged
        KillSwitch.cs                 --> Unchanged
        WindowsFilteringPlatform.cs   --> Unchanged

    Interop.Common\                   --> NEW (shared types)
        Interop.Common.csproj         --> net10.0 (cross-platform)
        Result.cs                     --> Extract Result<T>, Unit types
```

### 5.2 What Moves Where

| Component | Current Location | Proposed Location | Linux Server Reference |
|-----------|-----------------|-------------------|------------------------|
| `Result<T>` class | `WinTunAdapter.cs:408-423` | `Interop.Common\Result.cs` | Yes |
| `Unit` struct | `WinTunAdapter.cs:428-431` | `Interop.Common\Result.cs` | Yes |
| `WinTun` P/Invoke | `WinTun.cs` | `Interop` (unchanged) | No |
| `WinTunAdapter` | `WinTunAdapter.cs` | `Interop` (unchanged) | No |
| `KillSwitch` | `KillSwitch.cs` | `Interop` (unchanged) | No |
| `WindowsFilteringPlatform` | `WindowsFilteringPlatform.cs` | `Interop` (unchanged) | No |

### 5.3 Rationale

The `Result<T>` and `Unit` types defined in `WinTunAdapter.cs` are used by:
- `Vpn\Server\PacketRouter.fs:402,407` - for error handling
- `Vpn\Client\Tunnel.fs:46` - same pattern

These types are **not Windows-specific** and should be extracted to allow Linux server to use consistent error handling without referencing WinTun.

**Alternative:** Use F#'s built-in `Result` type and `unit` instead of C# equivalents. This would eliminate the need for Interop.Common entirely but requires code changes.

---

## 6. Other Necessary Splits

### 6.1 Wcf Library - Conditional Compilation

**File:** `Wcf\Program.fs:44`

**Issue:** `.UseWindowsService()` is Windows-only.

**Recommendation:** Conditional compilation:

```fsharp
#if WINDOWS
            .UseWindowsService()
#endif
```

**Alternative:** Create `Wcf.Linux` project that links all files except Program.fs, with a Linux-specific Program.fs that omits `.UseWindowsService()`.

### 6.2 Server Library - Platform-Specific Files

**Files requiring Linux alternatives:**

| File | Windows Behavior | Linux Behavior |
|------|------------------|----------------|
| `PacketRouter.fs` | Uses WinTunAdapter | Use Linux TUN/TAP (`/dev/net/tun`) |
| `ExternalInterface.fs:200` | `IOControl(ReceiveAll)` | Not needed on Linux |

**Recommendation:** Create platform-specific variants:

```
Vpn\Server\
    PacketRouter.fs              --> Windows (uses WinTunAdapter)
    PacketRouter.Linux.fs        --> Linux (uses /dev/net/tun or raw sockets)
    ExternalInterface.fs         --> Windows (with SIO_RCVALL)
    ExternalInterface.Linux.fs   --> Linux (without SIO_RCVALL)
```

**Alternative:** Conditional compilation within single files.

### 6.3 Sys Library - Already Handled by Android Pattern

The Sys library contains Windows-specific files (`WindowsApi.fs`, `ServiceInstaller.fs`) but these are not directly used by server packet processing. Following the Android pattern, a Linux server project can link only the needed files:

- `Primitives.fs` - Yes
- `Errors.fs` - Yes
- `Core.fs` - Yes
- `AppSettings.fs` - Yes
- `Crypto.fs` - Yes
- `Logging.fs` - Needs Linux variant (console-only, no log4net)
- `WindowsApi.fs` - No
- `ServiceInstaller.fs` - No

---

## 7. Final Deliverables

### 7.1 Concrete Recommendations

1. **Create `Interop.Common` project** with `Result<T>` and `Unit` types extracted from `WinTunAdapter.cs`

2. **Add conditional compilation** to `Wcf\Program.fs` for `.UseWindowsService()`:
   - Use `#if WINDOWS` / `#endif` around line 44

3. **Create Linux-specific server files**:
   - `PacketRouter.Linux.fs` - TUN/TAP via `/dev/net/tun` or raw sockets only
   - `ExternalInterface.Linux.fs` - Raw sockets without `IOControl(ReceiveAll)`

4. **Create `Linux\Sys` project** following Android pattern:
   - Link cross-platform Sys files
   - Provide Linux-specific `Logging.fs` (console-only)

5. **Create `Linux\Wcf` project** or use conditional compilation:
   - Exclude `Program.fs` Windows service hosting
   - Provide Linux-specific `Program.fs` with systemd support

6. **Create `VpnServerLinux` project** in main solution:
   - Target `net10.0` with `LINUX` symbol
   - Link server files, reference Linux-specific dependencies

### 7.2 Proposed Folder/Project Structure

```
C:\GitHub\Softellect\
    Linux\                                    --> NEW: Linux platform projects
        Sys\
            Sys.fsproj                        --> net10.0, LINUX symbol
            Logging.fs                        --> Linux-specific (console-only)
        Wcf\
            Wcf.fsproj                        --> net10.0, LINUX symbol
            Program.Linux.fs                  --> Without UseWindowsService()
        Server\
            Server.fsproj                     --> net10.0, LINUX symbol
            PacketRouter.Linux.fs             --> Linux TUN/TAP implementation
            ExternalInterface.Linux.fs        --> Without SIO_RCVALL

    Vpn\
        Interop.Common\                       --> NEW: Cross-platform types
            Interop.Common.csproj             --> net10.0
            Result.cs                         --> Result<T>, Unit

    Apps\Vpn\
        VpnServerLinux\                       --> NEW: Linux server executable
            VpnServerLinux.fsproj             --> net10.0, linux-x64 RID
            Program.fs                        --> Entrypoint (same as Windows)
```

### 7.3 Short-Term Actions (To Get Linux Server Compiling/Running)

1. **Extract `Result<T>` and `Unit`** from `WinTunAdapter.cs` into `Interop.Common`
   - Update `PacketRouter.fs` and `Tunnel.fs` to reference new location

2. **Add `#if WINDOWS` guard** to `Wcf\Program.fs:44` around `.UseWindowsService()`

3. **Create stub `Linux\Server\Server.fsproj`** that:
   - Links all server files except `PacketRouter.fs`
   - Provides placeholder `PacketRouter.Linux.fs` that logs "not implemented"
   - Does NOT reference `Interop.csproj`

4. **Create `Apps\Vpn\VpnServerLinux\VpnServerLinux.fsproj`** that:
   - References `Linux\Server\Server.fsproj`
   - Has `LINUX` compilation symbol
   - Targets `net10.0` with `linux-x64` runtime identifier

5. **Verify compilation** on Linux (AlmaLinux 9) with `dotnet build`

### 7.4 Deferred Refactor Items (NOT Part of This Phase)

The following items are **explicitly deferred** and should NOT be addressed in the initial migration:

1. **Clean DI boundaries** - Composition roots remain in libraries
2. **WinTun abstraction layer** - No `ITunAdapter` interface for now
3. **Kill-switch for Linux** - Client-only feature, use iptables/nftables later
4. **Systemd integration** - Use `Microsoft.Extensions.Hosting.Systemd` later
5. **Linux TUN/TAP implementation** - Start with raw sockets only
6. **Unified build pipeline** - Manual build initially
7. **Cross-platform testing** - Manual verification initially
8. **Package/deployment** - Manual deployment to AlmaLinux

---

## 8. Conclusion

Linux server migration is **feasible with minimal changes**. The key blockers are:

1. **WinTun adapter** - Not needed for server (can use raw sockets directly)
2. **Windows Service hosting** - Conditional compilation required

The Android client precedent demonstrates that platform separation via linked files and conditional compilation is a proven pattern in this codebase. The Linux server can follow the same approach with even less disruption since:

- Target framework is the same (`net10.0` vs `net10.0-android`)
- Server code is already more cross-platform than client code
- No GUI framework differences to manage

**Risk assessment: LOW** - The migration path is straightforward and follows established patterns.
