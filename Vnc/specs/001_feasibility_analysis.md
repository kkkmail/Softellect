# Softellect VNC - Feasibility Analysis & Recommendations

**Date:** 2026-02-15
**Status:** Draft

---

## 1. Goal

Build a private, simple remote desktop system (codename: **SoftellectVnc**) with:

- Full remote desktop (screen capture + display)
- Keyboard, mouse, and clipboard support
- File transfer
- Pre-login access (runs as a Windows service under SYSTEM)
- NAT traversal via a repeater (public-IP relay server, can be Linux)
- Pre-shared key authentication (machines are pre-authorized)
- Mostly F# with small C# interop for low-level Win32 APIs

Non-goals: multi-monitor negotiation, audio, printing, chat, scaling to thousands of concurrent sessions, cross-platform viewer (Windows-only is fine for now).

---

## 2. Existing Infrastructure Audit

### 2.1 What already exists and maps directly

| Capability | Existing Code | Reuse Assessment |
|---|---|---|
| **UDP data plane** | `Vpn/Core/UdpProtocol.fs` - push datagrams with AES per-packet encryption, bounded packet queues, reassembly, keepalive | **Direct reuse.** Screen frame data goes over UDP. The `BoundedPacketQueue`, `buildPushDatagram`, `tryParsePushDatagram`, `derivePacketAesKey` are all directly applicable. |
| **WCF control plane** | `Wcf/` - CoreWCF wrapper with NetTcp/Http bindings, FsPickler binary+gzip serialization, `tryReply`/`tryCommunicate` pattern | **Direct reuse.** Auth handshake, session management, clipboard sync, file transfer control messages all go here. |
| **Pre-shared key auth** | `Sys/Crypto.fs` - RSA 4096-bit keys, AES hybrid encryption, sign+encrypt/decrypt+verify. `Vpn/Core/KeyManagement.fs` - key import/export. `Vpn/Server/WcfServer.fs` - `tryDecryptAndVerifyRequest`/`trySignAndEncryptResponse` pattern. | **Direct reuse.** The exact same auth flow (client sends encrypted+signed request, server decrypts with private key, verifies with client public key, responds encrypted with client public key) works for VNC. |
| **Windows service hosting** | `Wcf/Program.fs` - `wcfMain` with `Host.CreateDefaultBuilder().UseWindowsService()`, `IHostedService` pattern. `Sys/ServiceInstaller.fs` - install/uninstall/start/stop. | **Direct reuse.** The VNC service host follows the same pattern as `VpnServer`. |
| **PowerShell deployment** | `Scripts/` - `Reinstall-WindowsService.ps1`, `Install-DistributedService.ps1`, `Start/Stop-WindowsService.ps1`, `VpnServerFunctions.ps1` pattern. | **Direct reuse.** Copy the VPN PS script pattern for VNC service deployment. |
| **C# interop pattern** | `Vpn/Interop/` - C# project with `AllowUnsafeBlocks`, P/Invoke declarations, `FSharpResult<T,string>` return types, consumed by F# `Core/` project. | **Direct reuse as a template.** The VNC interop project follows this exact pattern: C# project for Win32 P/Invoke, F# project wraps it. |
| **Error handling** | `Sys/Errors.fs` - hierarchical `SysError` DU, `Sys/Rop.fs` - railway-oriented programming | **Direct reuse.** Add `VncErr` case to `SysError` or define `VncError` DU in `Vnc/Core/Errors.fs` following the VPN pattern. |
| **Configuration** | `Sys/AppSettings.fs` - JSON-based config with `parseSimpleSetting`, `ConfigKey` pattern | **Direct reuse.** |
| **Logging** | `Sys/Logging.fs` - log4net wrapper | **Direct reuse.** |
| **Serialization** | `Sys/Core.fs` - `trySerialize`/`tryDeserialize` with `BinaryZippedFormat` | **Direct reuse.** For file transfer chunks and clipboard data. |

### 2.2 What needs to be built

| Component | Complexity | Notes |
|---|---|---|
| **Screen capture (DXGI)** | Medium | C# interop. Desktop Duplication API. ~300-500 lines of C#. |
| **Screen encoding** | Medium | Dirty-rect delta encoding + compression. F#. |
| **Input injection** | Low | C# interop. `SendInput` Win32 API. ~100-150 lines of C#. |
| **Clipboard sync** | Low | `GetClipboardData`/`SetClipboardData` or .NET `Clipboard` class. |
| **File transfer** | Low | Chunked byte stream over WCF. Follows existing serialization pattern. |
| **Repeater service** | Medium | UDP relay + WCF relay on public-IP server. New but architecturally simple. |
| **Viewer application** | Medium | WinForms/WPF app: renders frames, captures input, sends over control plane. |
| **Session management** | Low | Follows existing VPN `AuthService`/`ClientRegistry` pattern closely. |

---

## 3. Architecture

### 3.1 Components

```
+------------------+         +-------------------+        +------------------+
|   VNC Viewer     |         |  VNC Repeater     |        |  VNC Service     |
|   (WinForms)     |         |  (Linux/Windows)  |        |  (Win Service)   |
|                  |         |  Public IP         |        |  on target host  |
|  Renders screen  |<--UDP-->|  Relays UDP+WCF   |<--UDP->|  DXGI capture    |
|  Sends input     |--WCF-->|  between viewer    |--WCF-->|  SendInput       |
|  File transfer   |         |  and service       |        |  Clipboard       |
+------------------+         +-------------------+        +------------------+
```

### 3.2 Data Flows

**Screen capture flow (high-bandwidth, UDP):**
```
VNC Service                          Repeater                    VNC Viewer
    |                                    |                          |
    |-- DXGI captures dirty rects ------>|                          |
    |-- Encode (compress dirty rects) -->|                          |
    |-- UDP push datagram -------------->|-- relay ---------------->|
    |                                    |                          |-- Decode
    |                                    |                          |-- Render
```

**Input flow (low-bandwidth, WCF via repeater):**
```
VNC Viewer                           Repeater                    VNC Service
    |                                    |                          |
    |-- Mouse/key event (WCF) --------->|-- relay ---------------->|
    |                                    |                          |-- SendInput
```

**File transfer flow (WCF via repeater):**
```
Either direction, chunked over WCF, FsPickler serialized + gzipped.
```

**Clipboard flow (WCF via repeater):**
```
Bidirectional. Clipboard change detected -> serialize -> WCF -> apply on remote.
```

### 3.3 Repeater Design

The repeater is the NAT traversal solution. It is the equivalent of UltraVNC's repeater but integrated into your stack.

**How it works:**
1. VNC Service (behind NAT) connects **outbound** to the repeater on startup, establishing a persistent WCF control connection and registering its machine ID.
2. VNC Service also sends UDP keepalives to the repeater, establishing the NAT mapping for the UDP data plane (same pattern as your VPN client's keepalive).
3. VNC Viewer connects to the repeater, specifies which machine ID it wants to reach.
4. Repeater authenticates the viewer (pre-shared keys), then relays both WCF and UDP traffic between viewer and service.

**Key insight:** The VNC Service is the "client" of the repeater (it connects outbound). The viewer is also a "client" of the repeater. The repeater just pairs them. This is architecturally identical to how your VPN clients connect to the VPN server - the repeater IS the VPN server equivalent, but instead of routing IP packets to the internet, it routes VNC frames between paired endpoints.

**Repeater can run on Linux** because:
- CoreWCF works on Linux (your `Wcf/Program.fs` already has `#if LINUX` conditionals)
- UDP relay is platform-independent
- No DXGI or SendInput needed on the repeater

### 3.4 Pre-Login Access

Running as a Windows service under `NT AUTHORITY\SYSTEM` (same as your VPN server, see `VpnServerFunctions.ps1` line 24: `Install-DistributedService -ServiceName $ServiceName -Login "NT AUTHORITY\SYSTEM"`) provides:

- Starts before any user logs in
- Access to the secure desktop (Ctrl+Alt+Del, login screen)
- Survives user logoff/switch

For DXGI Desktop Duplication to work from a service on the secure desktop (pre-login, UAC prompts), the service must run in **Session 0** (which Windows services do by default) and needs to handle desktop switching. Specifically:

- **Session 0 isolation:** Since Vista, services run in Session 0 which has no interactive desktop. DXGI Desktop Duplication requires access to the active console session's desktop.
- **Solution:** The service spawns a helper process in the **console session** (Session 1+) using `CreateProcessAsUser` or `WTSQueryUserToken` + `CreateProcessAsUser`. This helper does the actual DXGI capture and communicates with the service via a local named pipe or shared memory.
- **For the login screen:** Use `OpenInputDesktop` / `SetThreadDesktop` to access the Winlogon desktop, or detect session changes via `WTSRegisterSessionNotification` and switch capture accordingly.
- **Alternative (simpler, slightly less capable):** Use `BitBlt` from the service process directly. This works for basic screen capture without GPU acceleration but does not capture hardware-accelerated content (DirectX overlays, video playback). For remote administration purposes this is often sufficient.

**Recommendation:** Start with the helper-process approach. It is the proven pattern used by UltraVNC and other tools for Session 0 service + interactive desktop capture.

---

## 4. C# Interop Components

Following the `Vpn/Interop/` pattern: a single C# project `Vnc/Interop/Interop.csproj` with `AllowUnsafeBlocks=true`, returning `FSharpResult<T, string>`.

### 4.1 Screen Capture (`DesktopDuplication.cs`)

**Win32/COM APIs needed:**
- `IDXGIOutputDuplication` (DXGI 1.2 Desktop Duplication API)
- `ID3D11Device`, `ID3D11DeviceContext`
- `AcquireNextFrame`, `MapSubresource` for pixel data
- `GetFrameDirtyRects`, `GetFrameMoveRects` for delta information

**What it provides to F#:**
```
CaptureFrame() -> FSharpResult<FrameData, string>

FrameData:
  - Width, Height, Stride
  - PixelData: byte[]  (or Memory<byte> for zero-copy)
  - DirtyRects: Rectangle[]
  - MoveRects: MoveRectangle[]
  - CursorPosition: Point
  - CursorShape: byte[] (optional)
```

**Size estimate:** ~400-500 lines of C#. Well-documented API with many reference implementations.

### 4.2 Input Injection (`InputInjector.cs`)

**Win32 APIs needed:**
- `SendInput` with `INPUT_MOUSE`, `INPUT_KEYBOARD`
- `MapVirtualKey` for scan code translation

**What it provides to F#:**
```
SendMouseEvent(x, y, buttons, wheel) -> FSharpResult<Unit, string>
SendKeyboardEvent(virtualKey, scanCode, isKeyUp, isExtended) -> FSharpResult<Unit, string>
```

**Size estimate:** ~100-150 lines of C#. Straightforward.

### 4.3 Clipboard (`ClipboardInterop.cs`)

**Approach:** Use `System.Windows.Forms.Clipboard` (available in .NET on Windows) or P/Invoke `OpenClipboard`/`GetClipboardData`/`SetClipboardData` for format control.

**What it provides to F#:**
```
GetClipboardContent() -> FSharpResult<ClipboardData, string>
SetClipboardContent(ClipboardData) -> FSharpResult<Unit, string>
AddClipboardListener(callback) -> FSharpResult<Unit, string>

ClipboardData:
  - Text: string option
  - Files: string[] option  (for file paths on clipboard)
```

**Size estimate:** ~100-200 lines of C#.

### 4.4 Session Helper (`SessionHelper.cs`)

**Win32 APIs needed:**
- `WTSGetActiveConsoleSessionId`
- `WTSQueryUserToken`
- `CreateProcessAsUser` / `DuplicateTokenEx`
- `WTSRegisterSessionNotification` / `WM_WTSSESSION_CHANGE`

**What it provides:**
- Launches the capture helper process in the active console session
- Detects session switches (login/logoff/lock/unlock)

**Size estimate:** ~200-300 lines of C#.

---

## 5. F# Components

### 5.1 Project Structure

```
Vnc/
  Core/
    Core.fsproj          -- References: Sys, Wcf
    Primitives.fs        -- VncMachineId, VncSessionId, FrameData, InputEvent, etc.
    Errors.fs            -- VncError DU
    ServiceInfo.fs       -- IVncService, IVncWcfService, IVncViewerService contracts
    AppSettings.fs       -- Configuration loading
    Protocol.fs          -- Frame encoding/decoding, delta compression
  Interop/
    Interop.csproj       -- C# interop (DXGI, SendInput, Clipboard, Session)
  Service/
    Service.fsproj       -- References: Core, Interop, Wcf
    CaptureService.fs    -- DXGI frame capture loop
    InputService.fs      -- Input injection handler
    ClipboardService.fs  -- Clipboard sync
    FileTransfer.fs      -- File transfer handler
    VncService.fs        -- Main service orchestrator (IHostedService)
    WcfServer.fs         -- WCF service implementation (encrypted)
    Program.fs           -- Service entry point (wcfMain pattern)
  Repeater/
    Repeater.fsproj      -- References: Core, Wcf
    RepeaterService.fs   -- Pairs viewer<->service, relays traffic
    UdpRelay.fs          -- UDP packet relay
    WcfRelay.fs          -- WCF message relay
    Program.fs           -- Repeater entry point
  Viewer/
    Viewer.fsproj        -- References: Core, Wcf (WinForms app)
    ScreenRenderer.fs    -- Renders received frames
    InputCapture.fs      -- Captures local mouse/keyboard
    ClipboardSync.fs     -- Local clipboard monitoring
    FileTransfer.fs      -- File send/receive UI
    ViewerForm.fs        -- Main form
    Program.fs           -- Viewer entry point
Apps/
  Vnc/
    VncService/          -- Publishable Windows service
    VncRepeater/         -- Publishable repeater (Linux-capable)
    VncViewer/           -- Publishable viewer app
```

### 5.2 Key Types (Primitives.fs)

```fsharp
type VncMachineId = VncMachineId of Guid      // Identifies a remote machine
type VncSessionId = VncSessionId of Guid       // Active viewer<->service session
type VncRepeaterAddress = { address: ServiceAddress; udpPort: ServicePort; wcfPort: ServicePort }

type FrameRegion =
    { x: int; y: int; width: int; height: int; data: byte[] }

type FrameUpdate =
    { sequenceNumber: uint64
      regions: FrameRegion[]
      cursorX: int; cursorY: int
      cursorShape: byte[] option }

type InputEvent =
    | MouseMove of x: int * y: int
    | MouseButton of x: int * y: int * button: MouseButton * isDown: bool
    | MouseWheel of x: int * y: int * delta: int
    | KeyPress of virtualKey: int * scanCode: int * isDown: bool * isExtended: bool

type ClipboardData =
    | TextClip of string
    | FileListClip of string[]

type FileChunk =
    { transferId: Guid
      filePath: string
      chunkIndex: int
      totalChunks: int
      data: byte[] }
```

### 5.3 WCF Service Contracts (ServiceInfo.fs)

Following the VPN's `IAuthWcfService` pattern (byte[] -> byte[] with encryption):

```fsharp
[<ServiceContract(ConfigurationName = "VncService")>]
type IVncWcfService =
    [<OperationContract>] abstract authenticate : data:byte[] -> byte[]
    [<OperationContract>] abstract sendInput : data:byte[] -> byte[]
    [<OperationContract>] abstract getClipboard : data:byte[] -> byte[]
    [<OperationContract>] abstract setClipboard : data:byte[] -> byte[]
    [<OperationContract>] abstract startFileTransfer : data:byte[] -> byte[]
    [<OperationContract>] abstract sendFileChunk : data:byte[] -> byte[]
    [<OperationContract>] abstract receiveFileChunk : data:byte[] -> byte[]
```

### 5.4 Frame Encoding (Protocol.fs)

**Strategy:** Dirty-rect encoding with compression.

1. DXGI gives us dirty rectangles (regions that changed since last frame).
2. For each dirty rect, extract pixel data from the frame buffer.
3. Compress each region with LZ4 (fast) or Zstd (better ratio). For a first version, `System.IO.Compression.BrotliStream` or GZip (already used in the codebase) is fine.
4. Serialize the `FrameUpdate` record.
5. Fragment into UDP push datagrams using the existing `buildPushDatagram` with reassembly on the viewer side.

**Bandwidth estimate:** A typical desktop at 1920x1080x32bpp = ~8MB/frame. With dirty rects (typically 1-10% of screen changes per frame), you're sending ~80KB-800KB per update. With compression, this drops to ~20KB-200KB. At 30fps with moderate changes, expect 1-6 Mbps sustained - well within your UDP plane's capacity.

**Adaptive quality:** Can optionally add JPEG/WebP encoding for dirty rects when bandwidth is constrained. This is an optimization, not required for v1.

---

## 6. Repeater Deep Dive

### 6.1 Registration Flow

```
1. VNC Service starts, reads config: repeaterAddress, machineId, pre-shared keys.
2. VNC Service connects to Repeater via WCF (outbound TCP):
   - Sends encrypted+signed registration: { machineId, publicKey }
   - Repeater verifies with stored public key, responds with OK + UDP port.
3. VNC Service begins sending UDP keepalives to Repeater's UDP port.
   - This establishes and maintains the NAT pinhole.
4. Repeater stores: machineId -> (WCF channel, UDP endpoint from keepalive source).
```

### 6.2 Viewer Connection Flow

```
1. Viewer starts, user selects target machineId.
2. Viewer connects to Repeater via WCF:
   - Sends encrypted+signed connect request: { targetMachineId, viewerPublicKey }
   - Repeater checks: is targetMachineId registered and online?
   - If yes: creates a VncSessionId, notifies the VNC Service via its WCF channel.
   - Responds to Viewer with: { sessionId, UDP port for frame data }
3. Viewer begins receiving UDP frame data relayed through Repeater.
4. Viewer sends input events via WCF through Repeater.
```

### 6.3 Relay Mechanism

**UDP relay:** Repeater receives UDP datagrams from VNC Service (screen frames), looks up the paired viewer by sessionId, and forwards them. Vice versa for any viewer->service UDP traffic. This is a simple packet-forwarding loop, no decryption needed (end-to-end encryption between viewer and service is optional since the transport is already encrypted per-packet with AES).

**WCF relay:** Repeater exposes a WCF service that both viewer and service connect to. For input events and control messages, the repeater receives from the viewer and forwards to the service's WCF callback or queues them for the service to poll.

### 6.4 Linux Compatibility

The repeater needs only:
- CoreWCF (works on Linux - already used in `VpnServerLinux`)
- `System.Net.Sockets.UdpClient` (cross-platform)
- No Win32 APIs

Your existing `#if LINUX` pattern in `Wcf/Program.fs` handles the `UseWindowsService()` vs plain host distinction.

---

## 7. Build Order & Milestones

### Phase 1: Screen Capture Proof of Concept (1-2 weeks)
1. `Vnc/Interop/` - DXGI Desktop Duplication wrapper in C#
2. `Vnc/Core/` - Frame data types, basic encoding (dirty rects + GZip)
3. Simple console app: capture screen, encode, decode, display in a WinForms window on the same machine
4. **Validates:** DXGI works, encoding pipeline, rendering

### Phase 2: Direct Connection (1-2 weeks)
5. `Vnc/Service/` - Windows service with DXGI capture loop + UDP sender
6. `Vnc/Viewer/` - WinForms app with UDP receiver + renderer
7. Input injection (SendInput interop) + input event WCF contract
8. Direct LAN connection (no repeater, no NAT): Viewer connects to Service by IP
9. **Validates:** End-to-end remote desktop over LAN

### Phase 3: Auth & Encryption (1 week)
10. Pre-shared key auth (reuse VPN's `tryDecryptAndVerifyRequest` pattern)
11. Session establishment over WCF
12. Per-packet AES on UDP frames (reuse `derivePacketAesKey`)
13. **Validates:** Secure connection

### Phase 4: Repeater (1-2 weeks)
14. `Vnc/Repeater/` - Registration, pairing, UDP relay, WCF relay
15. VNC Service connects outbound to repeater
16. Viewer connects to repeater, reaches service behind NAT
17. **Validates:** NAT traversal, full architecture

### Phase 5: Clipboard & File Transfer (1 week)
18. Clipboard sync (bidirectional, text + file list)
19. File transfer (chunked over WCF)
20. **Validates:** Complete feature set

### Phase 6: Pre-Login & Polish (1 week)
21. Session 0 helper process for secure desktop capture
22. PowerShell install/uninstall scripts (clone VPN pattern)
23. Reconnection logic, error recovery
24. **Validates:** Production-ready service

---

## 8. Feasibility Assessment

### Verdict: Highly Feasible

**Rationale:**

1. **~70% of the infrastructure already exists.** The UDP data plane, WCF control plane, crypto, auth, service hosting, deployment scripts, error handling, logging, and configuration are all production-tested in the VPN codebase. This is not a greenfield project - it is extending an existing platform with a new application (remote desktop instead of VPN tunneling).

2. **The new code is well-bounded.** The genuinely new pieces are:
   - DXGI screen capture (~500 lines C#)
   - SendInput injection (~150 lines C#)
   - Frame encoding (~300 lines F#)
   - Viewer rendering (~400 lines F#)
   - Repeater relay logic (~500 lines F#)
   - Total: ~2000-2500 lines of new code, not including tests and configuration

3. **No exotic dependencies.** DXGI Desktop Duplication is a stable, well-documented Windows API available since Windows 8. SendInput has been stable since Windows 2000. CoreWCF on Linux is proven in your VPN server. No third-party screen capture libraries needed.

4. **The repeater is architecturally simple.** It is essentially a matchmaker + packet forwarder. Your VPN server already does the hard part (UDP relay between clients). The repeater is a stripped-down version that pairs two endpoints instead of routing to the internet.

5. **Pre-login access is the hardest part**, but it's a solved problem. The Session 0 helper-process pattern is well-documented and used by UltraVNC, TightVNC, and others. The service itself already runs under SYSTEM (your VPN uses this).

### Risks

| Risk | Severity | Mitigation |
|---|---|---|
| DXGI Desktop Duplication on secure desktop (pre-login) | Medium | Phase 6. Start with logged-in desktop first. Helper-process pattern is well-documented. |
| Frame encoding performance at high resolution / high change rate | Low | DXGI dirty rects reduce data volume drastically. GZip/LZ4 compression is fast. Adaptive quality (JPEG for large rects) is an easy fallback. |
| Repeater throughput for screen data | Low | Screen data is 1-6 Mbps per session. A Linux server with 100 Mbps easily handles dozens of simultaneous sessions. Your UDP relay is already efficient. |
| Clipboard with large content (images) | Low | Cap clipboard sync to text and file paths initially. Image clipboard is a v2 feature. |
| CoreWCF stability for long-running relay connections | Low | Your VPN already runs CoreWCF connections 24/7. The WCF layer is battle-tested. |

---

## 9. Recommendations

1. **Start with Phase 1 (DXGI PoC).** The screen capture C# interop is the single piece with the most unknowns. Validate it first in isolation before building the full pipeline.

2. **Reuse, don't rewrite.** Reference `Softellect.Sys`, `Softellect.Wcf`, and `Softellect.Vpn.Core` directly. The VPN's UDP protocol, crypto, and service hosting code should be consumed as library dependencies, not copied.

3. **Follow the VPN's architectural pattern exactly.** The VNC project structure should mirror the VPN:
   - `Vnc/Core/` = `Vpn/Core/` (primitives, errors, protocol, app settings)
   - `Vnc/Interop/` = `Vpn/Interop/` (C# P/Invoke)
   - `Vnc/Service/` = `Vpn/Server/` (the remote host agent)
   - `Vnc/Repeater/` = new, but follows `Vpn/Server/` hosting pattern
   - `Vnc/Viewer/` = new, follows `Vpn/Client/` + WinForms UI
   - `Apps/Vnc/*` = `Apps/Vpn/*` (thin entry points + PS scripts)

4. **Use GZip for frame compression initially** since it's already integrated (`BinaryZippedFormat`). Switch to LZ4 or Zstd only if profiling shows GZip is the bottleneck (unlikely for dirty-rect data).

5. **For the repeater, reuse the VPN server's `UdpServer.fs` pattern** for the UDP relay loop and `WcfServer.fs` for the authenticated WCF endpoint. The repeater is a VPN server that routes VNC frames instead of IP packets.

6. **Defer multi-monitor support.** DXGI can enumerate outputs, but handling multiple monitors adds complexity to frame encoding and viewer rendering. Start with the primary monitor.

7. **Defer audio.** Not in the requirements, and it's a separate, complex pipeline (WASAPI capture + Opus encoding + jitter buffer). Can be added later without architectural changes.

---

## 10. Dependencies Summary

**NuGet packages needed (beyond what's already referenced):**
- None for core functionality. DXGI, SendInput, and clipboard APIs are all in Windows SDK (accessed via P/Invoke).
- Optional: `K4os.Compression.LZ4` for faster frame compression (v2 optimization).
- Optional: `System.Drawing.Common` or `SkiaSharp` for JPEG encoding of dirty rects (v2 optimization).

**Existing Softellect packages reused:**
- `Softellect.Sys` (crypto, errors, logging, config, service installer, core utilities)
- `Softellect.Wcf` (CoreWCF wrapper, client/service, serialization)
- `Softellect.Vpn.Core` (UDP protocol, bounded queues, reassembly - consider extracting the non-VPN-specific parts into `Softellect.Sys` or a shared transport library)

---

## 11. Open Questions for Owner

1. **Viewer technology:** WinForms (simplest, fastest to build) vs WPF (better rendering pipeline, supports GPU composition) vs bare SDL2/OpenGL (lowest latency but more work). **Recommendation: WinForms for v1.**

2. **Frame encoding:** Start with raw dirty rects + GZip, or invest in a codec (JPEG/WebP per rect) from the start? **Recommendation: GZip first, profile later.**

3. **Repeater deployment:** Single repeater, or support for multiple repeaters with failover? **Recommendation: Single repeater for v1.**

4. **Shared transport library:** The UDP push protocol (`UdpProtocol.fs`), `BoundedPacketQueue`, and `derivePacketAesKey` are not VPN-specific. Consider extracting them into `Softellect.Sys` or a new `Softellect.Transport` library so both VPN and VNC reference the same code without VNC depending on `Vpn.Core`. **Recommendation: Yes, do this before starting VNC development.**
