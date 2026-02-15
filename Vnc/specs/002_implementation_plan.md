# SoftellectVnc - Detailed Implementation Plan

**Date:** 2026-02-15
**Status:** Implementation ready
**Spec reference:** `Vnc/specs/001_feasibility_analysis.md`

---

## How to Use This Plan

- Each step has a checkbox `[ ]`. Mark `[x]` when completed.
- Steps within a phase are ordered — complete them sequentially.
- After each phase, there is a **verification gate** — do not proceed to the next phase until the gate passes.
- This plan is self-contained: all file paths, code patterns, and references are included so that a fresh session can pick up work from any checkpoint.

---

## Phase 0: Softellect.Transport Extraction

**Goal:** Extract `Vpn/Core/UdpProtocol.fs` into a new `Softellect.Transport` NuGet package. Both VPN and VNC will reference this package.

### Critical Design Note: VpnSessionId Dependency

`UdpProtocol.fs` currently references `VpnSessionId` from `Softellect.Vpn.Core.Primitives`. This type is defined as:
```fsharp
type VpnSessionId =
    | VpnSessionId of byte
    member this.value = let (VpnSessionId v) = this in v
```

During extraction, replace `VpnSessionId` with a new `PushSessionId` type in the Transport module:
```fsharp
type PushSessionId =
    | PushSessionId of byte
    member this.value = let (PushSessionId v) = this in v
```

Then in `Vpn/Core/Primitives.fs`, add a conversion:
```fsharp
type VpnSessionId =
    | VpnSessionId of byte
    member this.value = let (VpnSessionId v) = this in v
    member this.toPushSessionId = PushSessionId this.value
    static member fromPushSessionId (PushSessionId v) = VpnSessionId v
    // ... existing members ...
```

### Steps

- [ ] **0.1** Create directory `Transport/` at repo root (sibling of `Sys/`, `Wcf/`, etc.)

- [ ] **0.2** Create `Transport/Transport.fsproj`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Platforms>x64</Platforms>
    <AssemblyName>Softellect.Transport</AssemblyName>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <Authors>Konstantin Konstantinov</Authors>
    <Company>Softellect Systems, Inc.</Company>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <Version>10.0.102.0</Version>
    <PackageVersion>10.0.102.0</PackageVersion>
    <Description>Softellect Transport Library provides UDP push protocol, bounded packet queues, packet reassembly, and per-packet AES key derivation.</Description>
    <PackageTags>transport;udp;encryption;framework</PackageTags>
    <RepositoryUrl>https://github.com/kkkmail/Softellect</RepositoryUrl>
    <PackageProjectUrl>https://github.com/kkkmail/Softellect/tree/master/Transport</PackageProjectUrl>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|x64'">
    <PlatformTarget>x64</PlatformTarget>
    <DefineConstants>DEBUG</DefineConstants>
    <OtherFlags>--warnaserror+:25 --platform:x64</OtherFlags>
    <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|x64'">
    <PlatformTarget>x64</PlatformTarget>
    <OtherFlags>--warnaserror+:25 --platform:x64</OtherFlags>
    <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\README.md" Link="README.md" Pack="true" PackagePath="\" >
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <Compile Include="UdpProtocol.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sys\Sys.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Update="FSharp.Core" Version="10.0.102" />
  </ItemGroup>
</Project>
```

- [ ] **0.3** Copy `Vpn/Core/UdpProtocol.fs` → `Transport/UdpProtocol.fs` and modify:
  - Change namespace from `Softellect.Vpn.Core` to `Softellect.Transport`
  - Remove `open Softellect.Vpn.Core.Primitives`
  - Add `PushSessionId` type definition at the top of the module (before any functions that use it)
  - Replace all occurrences of `VpnSessionId` with `PushSessionId` throughout the file
  - The `open Softellect.Sys.Crypto` stays (for `AesKey` type)
  - Rename stats classes for generality:
    - `ClientPushStats` → `SenderPushStats`
    - `ServerPushStats` → `ReceiverPushStats`
    - Update `getSummary()` prefix strings accordingly: `"SENDER PUSH STATS:"`, `"RECEIVER PUSH STATS:"`
    - Keep all counter names the same internally

  **Exact opens in the new file:**
  ```fsharp
  namespace Softellect.Transport

  open System
  open System.Collections.Generic
  open System.Diagnostics
  open System.Security.Cryptography
  open System.Threading
  open Softellect.Sys.Crypto

  module UdpProtocol =
      type PushSessionId =
          | PushSessionId of byte
          member this.value = let (PushSessionId v) = this in v
          static member serverReserved = PushSessionId 0uy
      // ... rest of module with VpnSessionId → PushSessionId ...
  ```

- [ ] **0.4** Update `Vpn/Core/Core.fsproj`:
  - Remove `<Compile Include="UdpProtocol.fs" />` from the ItemGroup
  - Add `<ProjectReference Include="..\..\Transport\Transport.fsproj" />` to the ProjectReference ItemGroup

  **Before (lines 19-34):**
  ```xml
  <ItemGroup>
    <Compile Include="Primitives.fs" />
    <Compile Include="Errors.fs" />
    <Compile Include="KeyManagement.fs" />
    <Compile Include="PacketDebug.fs" />
    <Compile Include="ServiceInfo.fs" />
    <Compile Include="UdpProtocol.fs" />
    <Compile Include="AppSettings.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Sys\Sys.fsproj" />
    <ProjectReference Include="..\..\Wcf\Wcf.fsproj" />
  </ItemGroup>
  ```

  **After:**
  ```xml
  <ItemGroup>
    <Compile Include="Primitives.fs" />
    <Compile Include="Errors.fs" />
    <Compile Include="KeyManagement.fs" />
    <Compile Include="PacketDebug.fs" />
    <Compile Include="ServiceInfo.fs" />
    <Compile Include="AppSettings.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Sys\Sys.fsproj" />
    <ProjectReference Include="..\..\Wcf\Wcf.fsproj" />
    <ProjectReference Include="..\..\Transport\Transport.fsproj" />
  </ItemGroup>
  ```

- [ ] **0.5** Delete the old `Vpn/Core/UdpProtocol.fs` file (it now lives in `Transport/UdpProtocol.fs`).

- [ ] **0.6** Update `Vpn/Core/Primitives.fs` — add conversion helpers between `VpnSessionId` and `PushSessionId`:
  - Add `open Softellect.Transport.UdpProtocol` at the top
  - Add to the `VpnSessionId` type:
    ```fsharp
    member this.toPushSessionId = PushSessionId this.value
    static member fromPushSessionId (PushSessionId v) = VpnSessionId v
    ```

- [ ] **0.7** Update all VPN files that `open Softellect.Vpn.Core.UdpProtocol` → `open Softellect.Transport.UdpProtocol`. The **exact list** of files (from grep):
  - `Vpn/Server/UdpServer.fs` (line 15)
  - `Vpn/Server/PacketRouter.fs` (line 11)
  - `Vpn/Server/ExternalInterface.fs` (line 10)
  - `Vpn/Server/ClientRegistry.fs` (line 14)
  - `Vpn/Client/UdpClient.fs` (line 13)
  - `Vpn/Client/Tunnel.fs` (line 10)
  - `Vpn/LinuxServer/ExternalInterface_V03.fs` (line 10)
  - `Vpn/LinuxServer/ExternalInterface_V02.fs` (line 12)
  - `Vpn/LinuxServer/ExternalInterface_V01.fs` (line 10)
  - `Vpn/LinuxServer/ExternalInterface.fs` (line 10)

  In each file, change:
  ```fsharp
  open Softellect.Vpn.Core.UdpProtocol
  ```
  to:
  ```fsharp
  open Softellect.Transport.UdpProtocol
  ```

- [ ] **0.8** Fix all VpnSessionId ↔ PushSessionId mismatches in VPN code. After step 0.7, any VPN code that passes `VpnSessionId` to `buildPushDatagram` (which now expects `PushSessionId`) or receives `PushSessionId` from `tryParsePushDatagram` (which now returns `PushSessionId`) needs conversion. Search for:
  - Calls to `buildPushDatagram` — change `sessionId` argument to `sessionId.toPushSessionId`
  - Pattern matches on `tryParsePushDatagram` results — change `sessionId` to `VpnSessionId.fromPushSessionId sessionId`
  - Same for `ClientPushStats` → `SenderPushStats` and `ServerPushStats` → `ReceiverPushStats` renames

  **Strategy:** Build the solution and fix each compiler error. The errors will be self-documenting — type mismatches between `VpnSessionId` and `PushSessionId`, and missing type names for renamed stats classes.

- [ ] **0.9** Also check `Vpn/Server/Server.fsproj` and `Vpn/Client/Client.fsproj` — they may need `<ProjectReference Include="..\..\Transport\Transport.fsproj" />` if they don't already transitively get it via `Vpn/Core/Core.fsproj`. Check if the build resolves it transitively first.

- [ ] **0.10** Check `Vpn/LinuxServer/LinuxServer.fsproj` — same as 0.9.

- [ ] **0.11** Check the `Linux/Core/Core.fsproj` project (listed in `SoftellectMain.slnx` under `/Linux/` folder). If it also contains or references `UdpProtocol`, it needs the same treatment.

- [ ] **0.12** Add Transport to `PreBuild/IncrementBuildNumber.fsx`:
  - In the `projectsToUpdate` list (starts at line 15), add after the `("Wcf", ...)` entry:
  ```fsharp
  ("Transport", @"..\Transport\Transport.fsproj")
  ```

  **Exact context — current lines 15-18:**
  ```fsharp
  let projectsToUpdate = [
      ("Sys", "..\Sys\Sys.fsproj")
      ("Wcf", @"..\Wcf\Wcf.fsproj")
      ("Analytics", @"..\Analytics\Analytics.fsproj")
  ```
  **After:**
  ```fsharp
  let projectsToUpdate = [
      ("Sys", "..\Sys\Sys.fsproj")
      ("Wcf", @"..\Wcf\Wcf.fsproj")
      ("Transport", @"..\Transport\Transport.fsproj")
      ("Analytics", @"..\Analytics\Analytics.fsproj")
  ```

- [ ] **0.13** Add Transport to `copyPackages.bat` — add this line (after the Wcf line, line 14):
  ```bat
  copy /b /y .\Transport\bin\x64\Release\*.nupkg  ..\Packages\
  ```

- [ ] **0.14** Add Transport to `SoftellectMain.slnx`. The file uses XML format. Add a new project entry. Place it near the other root-level libraries (after `Wcf/Wcf.fsproj` around line 178):
  ```xml
  <Project Path="Transport/Transport.fsproj">
    <Platform Project="x64" />
  </Project>
  ```

- [ ] **0.15** Create `TransportTests/TransportTests.fsproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <AssemblyName>Softellect.Tests.TransportTests</AssemblyName>
      <IsPackable>false</IsPackable>
      <GenerateProgramFile>false</GenerateProgramFile>
      <IsTestProject>true</IsTestProject>
      <Platforms>x64</Platforms>
      <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    </PropertyGroup>
    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|x64'">
      <PlatformTarget>x64</PlatformTarget>
      <DefineConstants>DEBUG</DefineConstants>
      <OtherFlags>--warnaserror+:25 --platform:x64</OtherFlags>
      <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
    </PropertyGroup>
    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|x64'">
      <PlatformTarget>x64</PlatformTarget>
      <OtherFlags>--warnaserror+:25 --platform:x64</OtherFlags>
      <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
    </PropertyGroup>
    <ItemGroup>
      <Compile Include="UdpProtocolTests.fs" />
      <Compile Include="BoundedPacketQueueTests.fs" />
      <Compile Include="Program.fs" />
    </ItemGroup>
    <ItemGroup>
      <PackageReference Include="coverlet.collector" Version="6.0.4">
        <PrivateAssets>all</PrivateAssets>
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      </PackageReference>
      <PackageReference Include="FluentAssertions" Version="[7.2.0]" />
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
      <PackageReference Include="xunit" Version="2.9.3" />
      <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
        <PrivateAssets>all</PrivateAssets>
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      </PackageReference>
      <ProjectReference Include="..\Transport\Transport.fsproj" />
      <PackageReference Update="FSharp.Core" Version="10.0.102" />
    </ItemGroup>
  </Project>
  ```

- [ ] **0.16** Create `TransportTests/Program.fs`:
  ```fsharp
  module Program = let [<EntryPoint>] main _ = 0
  ```

- [ ] **0.17** Create `TransportTests/UdpProtocolTests.fs`:
  ```fsharp
  namespace Softellect.Tests.TransportTests

  open System
  open Xunit
  open FluentAssertions
  open Softellect.Transport.UdpProtocol

  module UdpProtocolTests =

      [<Fact>]
      let ``buildPushDatagram roundtrip`` () =
          let sessionId = PushSessionId 42uy
          let nonce = Guid.NewGuid()
          let payload = [| 1uy; 2uy; 3uy; 4uy; 5uy |]
          let datagram = buildPushDatagram sessionId nonce payload
          match tryParsePushDatagram datagram with
          | Ok (sid, n, p) ->
              sid.value.Should().Be(42uy, "") |> ignore
              n.Should().Be(nonce, "") |> ignore
              p.Should().BeEquivalentTo(payload, "") |> ignore
          | Error e -> failwith $"Parse failed: {e}"

      [<Fact>]
      let ``buildPushDatagram maxPayload`` () =
          let sessionId = PushSessionId 1uy
          let nonce = Guid.NewGuid()
          let payload = Array.zeroCreate PushMaxPayload
          let datagram = buildPushDatagram sessionId nonce payload
          datagram.Length.Should().Be(PushMtu, "") |> ignore

      [<Fact>]
      let ``buildPushDatagram oversize throws`` () =
          let sessionId = PushSessionId 1uy
          let nonce = Guid.NewGuid()
          let payload = Array.zeroCreate (PushMaxPayload + 1)
          let action = fun () -> buildPushDatagram sessionId nonce payload |> ignore
          action.Should().Throw<Exception>("") |> ignore

      [<Fact>]
      let ``tryParsePushDatagram tooShort`` () =
          let data = Array.zeroCreate (PushHeaderSize - 1)
          match tryParsePushDatagram data with
          | Error _ -> ()
          | Ok _ -> failwith "Should have returned Error for short data"

      [<Fact>]
      let ``buildPayload roundtrip`` () =
          let cmd = PushCmdData
          let data = [| 10uy; 20uy; 30uy |]
          let payload = buildPayload cmd data
          match tryParsePayload payload with
          | Ok (c, d) ->
              c.Should().Be(cmd, "") |> ignore
              d.Should().BeEquivalentTo(data, "") |> ignore
          | Error _ -> failwith "Parse failed"

      [<Fact>]
      let ``tryParsePayload empty`` () =
          match tryParsePayload [||] with
          | Error _ -> ()
          | Ok _ -> failwith "Should have returned Error for empty payload"

      [<Fact>]
      let ``derivePacketAesKey deterministic`` () =
          let sessionKey = Array.init 32 (fun i -> byte i)
          let nonce = Guid.NewGuid()
          let key1 = derivePacketAesKey sessionKey nonce
          let key2 = derivePacketAesKey sessionKey nonce
          key1.key.Should().BeEquivalentTo(key2.key, "") |> ignore
          key1.iv.Should().BeEquivalentTo(key2.iv, "") |> ignore

      [<Fact>]
      let ``derivePacketAesKey different nonces`` () =
          let sessionKey = Array.init 32 (fun i -> byte i)
          let key1 = derivePacketAesKey sessionKey (Guid.NewGuid())
          let key2 = derivePacketAesKey sessionKey (Guid.NewGuid())
          (key1.key = key2.key).Should().BeFalse("") |> ignore

      [<Fact>]
      let ``derivePacketAesKey keyLength`` () =
          let sessionKey = Array.init 32 (fun i -> byte i)
          let nonce = Guid.NewGuid()
          let aesKey = derivePacketAesKey sessionKey nonce
          aesKey.key.Length.Should().Be(32, "") |> ignore
          aesKey.iv.Length.Should().Be(16, "") |> ignore

      [<Fact>]
      let ``buildPushDatagram with various sessionIds`` () =
          for b in [0uy; 1uy; 127uy; 255uy] do
              let sessionId = PushSessionId b
              let nonce = Guid.NewGuid()
              let payload = [| 99uy |]
              let datagram = buildPushDatagram sessionId nonce payload
              match tryParsePushDatagram datagram with
              | Ok (sid, n, _) ->
                  sid.value.Should().Be(b, "") |> ignore
                  n.Should().Be(nonce, "") |> ignore
              | Error e -> failwith $"Failed for sessionId {b}: {e}"
  ```

- [ ] **0.18** Create `TransportTests/BoundedPacketQueueTests.fs`:
  ```fsharp
  namespace Softellect.Tests.TransportTests

  open Xunit
  open FluentAssertions
  open Softellect.Transport.UdpProtocol

  module BoundedPacketQueueTests =

      [<Fact>]
      let ``enqueue dequeue single`` () =
          let q = BoundedPacketQueue(1024, 100)
          let packet = [| 1uy; 2uy; 3uy |]
          q.enqueue(packet).Should().BeTrue("") |> ignore
          match q.tryDequeue() with
          | Some p -> p.Should().BeEquivalentTo(packet, "") |> ignore
          | None -> failwith "Expected a packet"

      [<Fact>]
      let ``enqueue dequeue fifo`` () =
          let q = BoundedPacketQueue(1024, 100)
          let p1 = [| 1uy |]
          let p2 = [| 2uy |]
          let p3 = [| 3uy |]
          q.enqueue(p1) |> ignore
          q.enqueue(p2) |> ignore
          q.enqueue(p3) |> ignore
          (q.tryDequeue().Value).Should().BeEquivalentTo(p1, "") |> ignore
          (q.tryDequeue().Value).Should().BeEquivalentTo(p2, "") |> ignore
          (q.tryDequeue().Value).Should().BeEquivalentTo(p3, "") |> ignore

      [<Fact>]
      let ``tryDequeue empty`` () =
          let q = BoundedPacketQueue(1024, 100)
          q.tryDequeue().Should().BeNull("") |> ignore

      [<Fact>]
      let ``headDrop maxPackets`` () =
          let q = BoundedPacketQueue(1024, 2)
          q.enqueue([| 1uy |]) |> ignore
          q.enqueue([| 2uy |]) |> ignore
          q.enqueue([| 3uy |]) |> ignore  // should drop [1]
          q.count.Should().Be(2, "") |> ignore
          (q.tryDequeue().Value).[0].Should().Be(2uy, "") |> ignore

      [<Fact>]
      let ``headDrop maxBytes`` () =
          let q = BoundedPacketQueue(5, 100)
          q.enqueue([| 1uy; 2uy; 3uy |]) |> ignore  // 3 bytes
          q.enqueue([| 4uy; 5uy; 6uy |]) |> ignore  // would be 6 total, drop first -> 3 bytes
          q.count.Should().Be(1, "") |> ignore
          (q.tryDequeue().Value).[0].Should().Be(4uy, "") |> ignore

      [<Fact>]
      let ``oversizePacket rejected`` () =
          let q = BoundedPacketQueue(5, 100)
          let big = Array.zeroCreate 10
          q.enqueue(big).Should().BeFalse("") |> ignore
          q.count.Should().Be(0, "") |> ignore

      [<Fact>]
      let ``dequeueMany partial`` () =
          let q = BoundedPacketQueue(1024, 100)
          q.enqueue([| 1uy |]) |> ignore
          q.enqueue([| 2uy |]) |> ignore
          let result = q.dequeueMany(5)
          result.Length.Should().Be(2, "") |> ignore

      [<Fact>]
      let ``dequeueMany limit`` () =
          let q = BoundedPacketQueue(1024, 100)
          for i in 0..9 do q.enqueue([| byte i |]) |> ignore
          let result = q.dequeueMany(3)
          result.Length.Should().Be(3, "") |> ignore
          q.count.Should().Be(7, "") |> ignore

      [<Fact>]
      let ``wait signaled`` () =
          let q = BoundedPacketQueue(1024, 100)
          q.enqueue([| 1uy |]) |> ignore
          q.wait(100).Should().BeTrue("") |> ignore

      [<Fact>]
      let ``wait timeout`` () =
          let q = BoundedPacketQueue(1024, 100)
          q.wait(50).Should().BeFalse("") |> ignore

      [<Fact>]
      let ``droppedCounters accurate`` () =
          let q = BoundedPacketQueue(5, 100)
          q.enqueue([| 1uy; 2uy; 3uy |]) |> ignore
          q.enqueue([| 4uy; 5uy; 6uy |]) |> ignore  // drops first (3 bytes)
          q.droppedPackets.Should().Be(1L, "") |> ignore
          q.droppedBytes.Should().Be(3L, "") |> ignore

      [<Fact>]
      let ``resetDropCounters`` () =
          let q = BoundedPacketQueue(5, 100)
          q.enqueue([| 1uy; 2uy; 3uy |]) |> ignore
          q.enqueue([| 4uy; 5uy; 6uy |]) |> ignore
          q.resetDropCounters()
          q.droppedPackets.Should().Be(0L, "") |> ignore
          q.droppedBytes.Should().Be(0L, "") |> ignore

      [<Fact>]
      let ``atomicCounter increment and add`` () =
          let c = AtomicCounter()
          c.increment()
          c.increment()
          c.add(5L)
          c.value.Should().Be(7L, "") |> ignore

      [<Fact>]
      let ``atomicCounter reset`` () =
          let c = AtomicCounter()
          c.add(42L)
          let prev = c.reset()
          prev.Should().Be(42L, "") |> ignore
          c.value.Should().Be(0L, "") |> ignore
  ```

- [ ] **0.19** Add TransportTests to `SoftellectMain.slnx` — inside the `/Tests/` folder (around line 123):
  ```xml
  <Project Path="TransportTests/TransportTests.fsproj">
    <Platform Project="x64" />
  </Project>
  ```

- [ ] **0.20** Build the entire solution:
  ```bash
  dotnet build SoftellectMain.slnx -c Debug -p:Platform=x64
  ```
  Fix any compile errors. The most likely issues are `VpnSessionId`/`PushSessionId` mismatches and renamed stats classes.

- [ ] **0.21** Run Transport tests:
  ```bash
  dotnet test TransportTests/TransportTests.fsproj -c Debug -p:Platform=x64
  ```

- [ ] **0.22** Run existing tests to verify no regressions:
  ```bash
  dotnet test SysTests/SysTests.fsproj -c Debug -p:Platform=x64
  ```

### Phase 0 Verification Gate
- [ ] Solution builds clean with zero errors
- [ ] All TransportTests pass
- [ ] All SysTests pass
- [ ] `Transport/bin/x64/Debug/Softellect.Transport.*.nupkg` exists

---

## Phase 1: VNC Core Types & Screen Capture PoC

**Goal:** Create `Vnc/Core/` with primitive types and `Vnc/Interop/` with DXGI screen capture. Build a console app that captures the screen, encodes, decodes, and displays in a WinForms window on the same machine.

### Steps

- [ ] **1.1** Create directory structure:
  ```
  Vnc/Core/
  Vnc/Interop/
  ```

- [ ] **1.2** Create `Vnc/Core/Core.fsproj`:
  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <AssemblyName>Softellect.Vnc.Core</AssemblyName>
      <Platforms>x64</Platforms>
      <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    </PropertyGroup>
    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|x64'">
      <OtherFlags>--warnaserror+:25 --platform:x64</OtherFlags>
      <PlatformTarget>x64</PlatformTarget>
      <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
    </PropertyGroup>
    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|x64'">
      <PlatformTarget>x64</PlatformTarget>
      <OtherFlags>--warnaserror+:25 --platform:x64</OtherFlags>
      <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
    </PropertyGroup>
    <ItemGroup>
      <Compile Include="Primitives.fs" />
      <Compile Include="Errors.fs" />
      <Compile Include="Protocol.fs" />
      <Compile Include="FileSystemTypes.fs" />
      <Compile Include="ServiceInfo.fs" />
      <Compile Include="AppSettings.fs" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\Sys\Sys.fsproj" />
      <ProjectReference Include="..\..\Wcf\Wcf.fsproj" />
      <ProjectReference Include="..\..\Transport\Transport.fsproj" />
    </ItemGroup>
    <ItemGroup>
      <PackageReference Update="FSharp.Core" Version="10.0.102" />
    </ItemGroup>
  </Project>
  ```

- [ ] **1.3** Create `Vnc/Core/Primitives.fs` — VNC-specific types. Follow `Vpn/Core/Primitives.fs` pattern:
  ```fsharp
  namespace Softellect.Vnc.Core

  open System
  open Softellect.Sys.Primitives
  open Softellect.Sys.AppSettings

  module Primitives =
      [<Literal>]
      let VncServiceName = "VncService"

      [<Literal>]
      let VncRepeaterServiceName = "VncRepeaterService"

      [<Literal>]
      let VncAdminServiceName = "VncAdminService"

      type VncMachineName =
          | VncMachineName of string
          member this.value = let (VncMachineName v) = this in v

      type VncMachineId =
          | VncMachineId of Guid
          member this.value = let (VncMachineId v) = this in v
          static member tryCreate (s: string) =
              match Guid.TryParse s with
              | true, g -> Some (VncMachineId g)
              | false, _ -> None
          static member create() = Guid.NewGuid() |> VncMachineId

      type VncSessionId =
          | VncSessionId of Guid
          member this.value = let (VncSessionId v) = this in v
          static member create() = Guid.NewGuid() |> VncSessionId

      type VncMachineStatus =
          | Online
          | Offline
          | Unknown

      type VncMachineInfo =
          {
              machineName : VncMachineName
              machineId : VncMachineId
              status : VncMachineStatus
          }

      type MouseButton =
          | LeftButton
          | RightButton
          | MiddleButton

      type FrameRegion =
          {
              x : int
              y : int
              width : int
              height : int
              data : byte[]
          }

      type MoveRegion =
          {
              x : int
              y : int
              width : int
              height : int
              sourceX : int
              sourceY : int
          }

      type FrameUpdate =
          {
              sequenceNumber : uint64
              screenWidth : int
              screenHeight : int
              regions : FrameRegion[]
              moveRegions : MoveRegion[]
              cursorX : int
              cursorY : int
              cursorShape : byte[] option
          }

      type InputEvent =
          | MouseMove of x: int * y: int
          | MouseButton of x: int * y: int * button: MouseButton * isDown: bool
          | MouseWheel of x: int * y: int * delta: int
          | KeyPress of virtualKey: int * scanCode: int * isDown: bool * isExtended: bool

      type ClipboardData =
          | TextClip of string
          | FileListClip of string[]
  ```

- [ ] **1.4** Create `Vnc/Core/Errors.fs` — follow `Vpn/Core/Errors.fs` pattern:
  ```fsharp
  namespace Softellect.Vnc.Core

  open Softellect.Sys.Errors
  open Softellect.Wcf.Errors
  open Softellect.Vnc.Core.Primitives

  module Errors =
      type VncWcfError =
          | VncAuthWcfErr of WcfError
          | VncControlWcfErr of WcfError
          | VncFileTransferWcfErr of WcfError
          | VncRepeaterWcfErr of WcfError

      type VncCaptureError =
          | DxgiInitErr of string
          | FrameCaptureErr of string
          | EncodingErr of string

      type VncInputError =
          | SendInputErr of string
          | ClipboardErr of string

      type VncConnectionError =
          | RepeaterUnreachableErr of string
          | MachineOfflineErr of VncMachineName
          | AuthFailedErr of string
          | SessionExpiredErr of VncSessionId

      type VncFileTransferError =
          | DirectoryListErr of string
          | FileReadErr of string
          | FileWriteErr of string
          | TransferCancelledErr

      type VncError =
          | VncAggregateErr of VncError * List<VncError>
          | VncCaptureErr of VncCaptureError
          | VncInputErr of VncInputError
          | VncConnectionErr of VncConnectionError
          | VncFileTransferErr of VncFileTransferError
          | VncWcfErr of VncWcfError
          | VncConfigErr of string
          | VncCryptoErr of CryptoError
          | VncGeneralErr of string

          static member addError a b =
              match a, b with
              | VncAggregateErr (x, w), VncAggregateErr (y, z) -> VncAggregateErr (x, w @ (y :: z))
              | VncAggregateErr (x, w), _ -> VncAggregateErr (x, w @ [b])
              | _, VncAggregateErr (y, z) -> VncAggregateErr (a, y :: z)
              | _ -> VncAggregateErr (a, [b])

          static member (+) (a, b) = VncError.addError a b
          member a.add b = a + b

      type VncResult<'T> = Result<'T, VncError>
      type VncUnitResult = Result<unit, VncError>
  ```

- [ ] **1.5** Create `Vnc/Core/Protocol.fs` — frame encoding/decoding:
  - GZip compression for dirty rect data (use `Softellect.Sys.Core.trySerialize`/`tryDeserialize` with `BinaryZippedFormat`)
  - `encodeFrameUpdate : FrameUpdate -> byte[]`
  - `decodeFrameUpdate : byte[] -> Result<FrameUpdate, VncError>`
  - CopyRect optimization: `MoveRegion` sent as coordinates only

- [ ] **1.6** Create `Vnc/Core/FileSystemTypes.fs` — types for FAR-like file manager:
  ```fsharp
  namespace Softellect.Vnc.Core

  open System

  module FileSystemTypes =
      type FileEntryKind =
          | FileEntry
          | DirectoryEntry
          | ParentDirectory

      type FileEntry =
          {
              name : string
              kind : FileEntryKind
              size : int64
              lastModified : DateTime
              isSelected : bool
          }

      type DirectoryListing =
          {
              path : string
              entries : FileEntry[]
              error : string option
          }

      type FileTransferId =
          | FileTransferId of Guid
          member this.value = let (FileTransferId v) = this in v
          static member create() = Guid.NewGuid() |> FileTransferId

      type FileTransferDirection =
          | LocalToRemote
          | RemoteToLocal

      type FileChunk =
          {
              transferId : FileTransferId
              filePath : string
              chunkIndex : int
              totalChunks : int
              data : byte[]
          }

      type FileTransferRequest =
          {
              transferId : FileTransferId
              direction : FileTransferDirection
              files : string list
              destinationPath : string
          }
  ```

- [ ] **1.7** Create `Vnc/Core/ServiceInfo.fs` — WCF service contracts (stub, expand in Phase 2):
  - Follow `Vpn/Core/ServiceInfo.fs` pattern
  - `IVncWcfService` with `[<ServiceContract>]` and `byte[] -> byte[]` methods
  - High-level F# interfaces: `IVncService`, `IVncRepeaterService`

- [ ] **1.8** Create `Vnc/Core/AppSettings.fs` — configuration loading (stub, expand later):
  - `ConfigSection.vncMachines` following `ConfigSection.vpnConnections` pattern
  - `loadVncMachines()` following `loadVpnConnections()` pattern

- [ ] **1.9** Create `Vnc/Interop/Interop.csproj` — C# interop project (follow `Vpn/Interop/Interop.csproj`):
  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <AssemblyName>Softellect.Vnc.Interop</AssemblyName>
      <Platforms>x64</Platforms>
      <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
      <Nullable>enable</Nullable>
      <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    </PropertyGroup>
    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|x64'">
      <PlatformTarget>x64</PlatformTarget>
      <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
    </PropertyGroup>
    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|x64'">
      <PlatformTarget>x64</PlatformTarget>
      <NoWarn>NU5100;NU5110;NU5111;NU1903</NoWarn>
    </PropertyGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\Sys\Sys.fsproj" />
      <ProjectReference Include="..\Core\Core.fsproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **1.10** Evaluate Vortice.DirectX for DXGI Desktop Duplication. Check if `Vortice.DXGI` and `Vortice.Direct3D11` NuGet packages provide clean Desktop Duplication API access. If yes, use them in the Interop project. If not (overhead, limitations), use raw COM interop / P/Invoke.

- [ ] **1.11** Create `Vnc/Interop/DesktopDuplication.cs` — DXGI screen capture:
  - `CaptureFrame() → FSharpResult<FrameData, string>` (follow `WinTunAdapter.cs` pattern)
  - `FrameData`: Width, Height, Stride, PixelData (byte[]), DirtyRects (Rectangle[]), MoveRects, CursorPosition, CursorShape
  - Initialize D3D11 device, get DXGI output, create `OutputDuplication`
  - `AcquireNextFrame`, `MapSubresource`, `GetFrameDirtyRects`, `GetFrameMoveRects`

- [ ] **1.12** Create `Vnc/Interop/InputInjector.cs` — input injection:
  - `SendMouseEvent(x, y, buttons, wheel) → FSharpResult<Unit, string>`
  - `SendKeyboardEvent(virtualKey, scanCode, isKeyUp, isExtended) → FSharpResult<Unit, string>`
  - Uses `SendInput` Win32 API

- [ ] **1.13** Create `Vnc/Interop/ClipboardInterop.cs` — clipboard access:
  - `GetClipboardContent() → FSharpResult<ClipboardData, string>`
  - `SetClipboardContent(ClipboardData) → FSharpResult<Unit, string>`

- [ ] **1.14** Add all new VNC projects to `SoftellectMain.slnx`:
  ```xml
  <Folder Name="/Vnc/">
    <Project Path="Vnc/Core/Core.fsproj">
      <Platform Project="x64" />
    </Project>
    <Project Path="Vnc/Interop/Interop.csproj">
      <Platform Project="x64" />
    </Project>
  </Folder>
  ```

- [ ] **1.15** Build and verify:
  ```bash
  dotnet build SoftellectMain.slnx -c Debug -p:Platform=x64
  ```

- [ ] **1.16** Create a simple PoC console app (can be temporary, in `Vnc/PoC/` or as a test) that:
  1. Captures a frame using `DesktopDuplication`
  2. Encodes dirty rects via `Protocol.fs`
  3. Decodes
  4. Displays in a WinForms window

### Phase 1 Verification Gate
- [ ] Vnc/Core builds
- [ ] Vnc/Interop builds
- [ ] DXGI captures frames on the local machine
- [ ] Encode/decode round-trip preserves frame data

---

## Phase 2: Direct LAN Connection

**Goal:** VNC Service captures screen and sends over UDP. VNC Viewer receives and renders. Input injection works. Direct LAN, no repeater, no encryption.

### Steps

- [ ] **2.1** Create `Vnc/Service/Service.fsproj` — references Core, Interop, Wcf, Transport
- [ ] **2.2** Create `Vnc/Service/CaptureService.fs` — DXGI frame capture loop on a background thread
- [ ] **2.3** Create `Vnc/Service/InputService.fs` — handles incoming input events, calls `InputInjector`
- [ ] **2.4** Create `Vnc/Service/VncService.fs` — main `IHostedService` orchestrator
- [ ] **2.5** Create `Vnc/Service/WcfServer.fs` — WCF service implementation (follow `Vpn/Server/WcfServer.fs`)
- [ ] **2.6** Create `Vnc/Service/Program.fs` — service entry point (follow `Vpn/Server/Program.fs` + `Wcf/Program.fs` `wcfMain` pattern)
- [ ] **2.7** Create `Vnc/Viewer/Viewer.fsproj` — WinForms app, references Core, Wcf, Transport
- [ ] **2.8** Create `Vnc/Viewer/ScreenRenderer.fs` — renders received `FrameUpdate` to a `PictureBox` or custom-drawn `Panel`
- [ ] **2.9** Create `Vnc/Viewer/InputCapture.fs` — captures local mouse/keyboard events, sends to service via WCF
- [ ] **2.10** Create `Vnc/Viewer/ViewerForm.fs` — main remote desktop form
- [ ] **2.11** Create `Vnc/Viewer/Program.fs` — viewer entry point
- [ ] **2.12** UDP sender in Service: uses `buildPushDatagram` from `Softellect.Transport.UdpProtocol` to send frame updates
- [ ] **2.13** UDP receiver in Viewer: receives datagrams, reassembles, decodes `FrameUpdate`
- [ ] **2.14** WCF control channel: input events (mouse, keyboard) sent from viewer → service via WCF
- [ ] **2.15** Test end-to-end: viewer connects to service by IP on LAN, sees remote desktop, can control mouse/keyboard

### Phase 2 Verification Gate
- [ ] Remote desktop works over LAN (direct IP)
- [ ] Mouse and keyboard input works
- [ ] Frame rate is acceptable (>10fps for typical desktop activity)

---

## Phase 3: Auth & Encryption

**Goal:** Pre-shared key authentication and per-packet AES encryption.

### Steps

- [ ] **3.1** Add pre-shared key auth to VNC Service — follow `Vpn/Server/WcfServer.fs` `tryDecryptAndVerifyRequest`/`trySignAndEncryptResponse` pattern
- [ ] **3.2** Add auth client to VNC Viewer — follow VPN client auth flow
- [ ] **3.3** Session establishment: viewer authenticates with service, receives session AES key
- [ ] **3.4** Per-packet AES on UDP frames — use `derivePacketAesKey` from `Softellect.Transport.UdpProtocol`
- [ ] **3.5** Key generation tooling — create a simple utility (or admin command) to generate RSA key pairs for machines

### Phase 3 Verification Gate
- [ ] Unauthenticated connections are rejected
- [ ] Authenticated connections work with encrypted frames
- [ ] Keys can be generated and distributed

---

## Phase 4: Repeater

**Goal:** NAT traversal via repeater. VNC Service and Viewer both connect outbound to repeater.

### Steps

- [ ] **4.1** Create `Vnc/Repeater/Repeater.fsproj` — references Core, Wcf (NO Win32 dependencies)
- [ ] **4.2** Create `Vnc/Repeater/RepeaterService.fs`:
  - Machine registry: `machineName → (machineId, WCF channel, UDP endpoint, lastSeen)`
  - Registration handler (VNC Service registers with repeater on startup)
  - Connection handler (Viewer requests connection to a machine)
  - Status query handler (Viewer queries online/offline status)
  - Session pairing: creates `VncSessionId`, notifies both parties
- [ ] **4.3** Create `Vnc/Repeater/UdpRelay.fs`:
  - Receives UDP datagrams from Service, forwards to paired Viewer (and vice versa)
  - Simple packet-forwarding loop, no decryption (end-to-end encrypted)
  - Uses `System.Net.Sockets.UdpClient` (cross-platform)
- [ ] **4.4** Create `Vnc/Repeater/WcfRelay.fs`:
  - WCF service that both viewer and service connect to
  - Forwards input events from viewer to service
  - Forwards control messages
- [ ] **4.5** Create `Vnc/Repeater/Program.fs`:
  - Follow `Wcf/Program.fs` `wcfMain` pattern
  - `#if LINUX` for `.UseWindowsService()` conditional
- [ ] **4.6** Update VNC Service to connect outbound to repeater:
  - On startup: WCF connect to repeater, register machine name
  - Send UDP keepalives to maintain NAT pinhole
- [ ] **4.7** Update VNC Viewer to connect via repeater:
  - Connect to repeater, request connection to machine by name
  - Receive session info (sessionId, UDP port)
- [ ] **4.8** Create `Vnc/Viewer/MachineListForm.fs`:
  - Load machines from `appsettings.json` `"vncMachines"` section
  - Query repeater for status (batch WCF call)
  - Display list with Online/Offline indicators
  - Double-click to connect
- [ ] **4.9** Create App deployment projects:
  ```
  Apps/Vnc/VncService/    — publishable Windows service
  Apps/Vnc/VncRepeater/   — publishable repeater
  Apps/Vnc/VncViewer/     — publishable viewer
  ```
  Each follows the `Apps/Vpn/VpnServer/` pattern with `appsettings.json` and PS scripts.

### Phase 4 Verification Gate
- [ ] Viewer connects to Service behind NAT via repeater
- [ ] Machine list shows online/offline status
- [ ] Full remote desktop works through repeater

---

## Phase 5: Clipboard & File Transfer

**Goal:** Bidirectional clipboard sync and FAR-like dual-panel file manager.

### Steps

- [ ] **5.1** Create `Vnc/Service/ClipboardService.fs` — monitors clipboard changes, sends/receives via WCF
- [ ] **5.2** Create `Vnc/Viewer/ClipboardSync.fs` — viewer-side clipboard monitoring
- [ ] **5.3** Implement bidirectional clipboard sync: text + file paths (not images in v1)
- [ ] **5.4** Create `Vnc/Service/FileService.fs`:
  - `listDirectory(path) → DirectoryListing`
  - `readFileChunk(path, offset, size) → FileChunk`
  - `writeFileChunk(FileChunk) → Result`
  - `createDirectory(path) → Result`
  - `deleteFiles(paths) → Result`
- [ ] **5.5** Create `Vnc/Viewer/FileManagerForm.fs` — FAR-like dual-panel file manager:

  **Layout:**
  ```
  +---LOCAL PANEL---+---REMOTE PANEL--+
  |  path bar       |  path bar       |
  |  file list      |  file list      |
  |  (DataGridView) |  (DataGridView) |
  +---status bar----+---status bar----+
  | F5 Copy  F6 Move  F7 MkDir  F8 Delete  INS Select  Grey+ Pattern |
  +------------------------------------------------------------------+
  ```

  **Key bindings (FAR-style, NOT Explorer-style):**
  - **INS** — toggle selection on current item, move cursor down (sticky)
  - **Grey +** (numpad plus) — open pattern dialog to select matching files (e.g. `*.txt`)
  - **Grey -** (numpad minus) — deselect by pattern
  - **Grey *** (numpad asterisk) — invert selection
  - **F5** — copy selected from active panel to other panel's directory
  - **F6** — move selected
  - **F7** — create directory
  - **F8** — delete selected (with confirmation dialog)
  - **Enter** — on directory: navigate into; on `..`: navigate up
  - **Tab** — switch active panel
  - Selected items marked with `*` prefix or highlight color
  - Selection persists while navigating (NOT cleared on click like Explorer)

- [ ] **5.6** Implement chunked file transfer over WCF:
  - Progress bar during transfers
  - FsPickler serialized + gzipped (existing `BinaryZippedFormat`)
  - Both directions: local→remote and remote→local
- [ ] **5.7** Wire F5/F6 to actual transfer operations with progress feedback

### Phase 5 Verification Gate
- [ ] Clipboard text syncs bidirectionally
- [ ] File manager shows local and remote file systems
- [ ] INS, Grey+, Grey-, Grey* selection works correctly
- [ ] F5 copy transfers files in both directions with progress bar

---

## Phase 6: Pre-Login & Polish

**Goal:** Session 0 helper process for secure desktop capture, service deployment scripts, reconnection.

### Steps

- [ ] **6.1** Create `Vnc/Interop/SessionHelper.cs`:
  - `WTSGetActiveConsoleSessionId` — get active console session
  - `WTSQueryUserToken` + `CreateProcessAsUser` — launch helper in console session
  - `WTSRegisterSessionNotification` — detect session changes
  - Service monitors `SERVICE_CONTROL_SESSIONCHANGE` to relaunch helper on login/logoff/switch

- [ ] **6.2** Implement helper process architecture:
  - VNC Service (Session 0) spawns a capture helper in the console session
  - Helper does DXGI capture and communicates with service via named pipe
  - On session change: kill old helper, spawn new one in new session
  - For login screen: helper launched with SYSTEM token in Winlogon session

- [ ] **6.3** Create PowerShell deployment scripts (follow `Apps/Vpn/VpnServer/VpnServerFunctions.ps1`):
  - `VncServiceFunctions.ps1` — install/uninstall/start/stop under `NT AUTHORITY\SYSTEM`
  - `VncRepeaterFunctions.ps1` — same for repeater
  - Use `Install-DistributedService -ServiceName $ServiceName -Login "NT AUTHORITY\SYSTEM"`

- [ ] **6.4** Add reconnection logic:
  - Viewer: auto-reconnect on connection loss (with exponential backoff)
  - Service: auto-reconnect to repeater on connection loss
  - UDP keepalive failure detection → reconnect

- [ ] **6.5** Error recovery:
  - DXGI device lost → reinitialize
  - WCF channel faulted → recreate
  - Session timeout → re-authenticate

- [ ] **6.6** Final integration testing:
  - Service installed as Windows service
  - Pre-login access works (login screen visible)
  - Fast user switching works (helper relaunched)
  - Full remote desktop through repeater with encryption
  - File transfer works
  - Clipboard works

### Phase 6 Verification Gate
- [ ] VNC Service runs as Windows service under SYSTEM
- [ ] Pre-login desktop capture works
- [ ] Session switching relaunches helper correctly
- [ ] PS scripts install/uninstall the service correctly
- [ ] Reconnection works after network interruption

---

## Reference: Key Existing Code Patterns

### Pattern: WCF Service Host (from `Wcf/Program.fs`)
```fsharp
type ProgramData<'IService, 'WcfService> =
    {
        serviceAccessInfo : ServiceAccessInfo
        getService : unit -> 'IService
        getWcfService : 'IService -> 'WcfService
        saveSettings : unit -> unit
        configureServices : (IServiceCollection -> unit) option
        configureServiceLogging : ILoggingBuilder -> unit
        configureLogging : ILoggingBuilder -> unit
        postBuildHandler : (ServiceAccessInfo -> IHost -> unit) option
    }

// Usage:
wcfMain<IAuthService, IAuthWcfService, AuthWcfService> ProgramName programData argv
```

### Pattern: Encrypted WCF (from `Vpn/Server/WcfServer.fs`)
```fsharp
let inline private tryDecryptAndVerifyRequest<'T> serverData data verifier =
    match tryDecrypt encryptionType data serverData.serverPrivateKey with
    | Ok r -> // extract clientId, load public key, verify signature, deserialize
    | Error e -> ...

let inline private trySignAndEncryptResponse serverData clientKey response =
    match trySerialize wcfSerializationFormat response with
    | Ok responseBytes -> trySignAndEncrypt encryptionType responseBytes ...
    | Error e -> ...
```

### Pattern: Named Connections Config (from `Vpn/Core/AppSettings.fs`)
```fsharp
type ConfigSection
    with
    static member vpnConnections = ConfigSection "vpnConnections"

let loadVpnConnections () =
    match AppSettingsProvider.tryCreate ConfigSection.vpnConnections with
    | Ok provider ->
        match provider.tryGetSectionKeys () with
        | Ok keys -> keys |> List.map (fun k -> ...)
        | Error e -> []
    | Error e -> []
```

VNC machines config will follow the same pattern with `ConfigSection "vncMachines"`.

### Pattern: C# Interop (from `Vpn/Interop/WinTunAdapter.cs`)
```csharp
using Microsoft.FSharp.Core;
// Return FSharpResult to F#:
public static FSharpResult<ITunAdapter, string> Create(...) {
    // ... P/Invoke ...
    if (failed)
        return FSharpResult<ITunAdapter, string>.NewError("...");
    return FSharpResult<ITunAdapter, string>.NewOk(adapter);
}
```

### Pattern: Service Entry Point (from `Vpn/Server/Program.fs`)
```fsharp
let vpnServerMain argv =
    setLogLevel()
    let accessInfo = loadServerAccessInfo()
    match loadKeys keyPath with
    | Ok (privateKey, publicKey) ->
        let data = { ... }
        let program = getProgram data argv
        program()
    | Error msg -> Logger.logCrit msg; CriticalError
```

### Pattern: Test Project (from `SysTests/`)
```fsharp
// Program.fs
module Program = let [<EntryPoint>] main _ = 0

// Tests.fs
namespace Softellect.Tests.SysTests
open Xunit
open FluentAssertions

module CoreTests =
    [<Fact>]
    let someTest () =
        // Arrange, Act, Assert using FluentAssertions
        result.Should().BeTrue("") |> ignore
```

### Pattern: Solution File Entry (`.slnx` format)
```xml
<Folder Name="/FolderName/">
  <Project Path="relative/path/to/Project.fsproj">
    <Platform Project="x64" />
  </Project>
</Folder>
```

---

## Reference: Files That Import UdpProtocol (for Phase 0)

These files currently have `open Softellect.Vpn.Core.UdpProtocol` and must be changed to `open Softellect.Transport.UdpProtocol`:

| File | Line |
|---|---|
| `Vpn/Server/UdpServer.fs` | 15 |
| `Vpn/Server/PacketRouter.fs` | 11 |
| `Vpn/Server/ExternalInterface.fs` | 10 |
| `Vpn/Server/ClientRegistry.fs` | 14 |
| `Vpn/Client/UdpClient.fs` | 13 |
| `Vpn/Client/Tunnel.fs` | 10 |
| `Vpn/LinuxServer/ExternalInterface_V03.fs` | 10 |
| `Vpn/LinuxServer/ExternalInterface_V02.fs` | 12 |
| `Vpn/LinuxServer/ExternalInterface_V01.fs` | 10 |
| `Vpn/LinuxServer/ExternalInterface.fs` | 10 |

Additionally, after the rename of `ClientPushStats` → `SenderPushStats` and `ServerPushStats` → `ReceiverPushStats`, grep for those type names in the VPN codebase and update all references.

---

## Reference: Current UdpProtocol.fs Dependencies

The file `Vpn/Core/UdpProtocol.fs` (427 lines) has these dependencies:
- `System`, `System.Collections.Generic`, `System.Diagnostics`, `System.Security.Cryptography`, `System.Threading` — standard .NET
- `Softellect.Sys.Crypto` — for `AesKey` type (stays as-is in Transport)
- `Softellect.Vpn.Core.Primitives` — **only** for `VpnSessionId` type (replaced with `PushSessionId` in Transport)

The `VpnSessionId` type is used in exactly 4 places in `UdpProtocol.fs`:
1. Line 125: `let private packByteAndGuid (VpnSessionId b) (g: Guid) : byte[] =`
2. Line 176: `Ok (VpnSessionId b, Guid(guidBytes))`
3. Line 181: `let buildPushDatagram (sessionId: VpnSessionId) (nonce: Guid) (payload: byte[]) : byte[] =`
4. Line 212: `// let sessionId = data[0] |> VpnSessionId` (commented out, can be deleted)

---

## Reference: Version Info

- Current version line: `10.0.102.XX` (XX is managed by `IncrementBuildNumber.fsx`)
- FSharp.Core: `10.0.102`
- Target framework: `net10.0`
- Platform: `x64` only
- Build command: `dotnet build SoftellectMain.slnx -c Release -p:Platform=x64`
- Test command: `dotnet test <project>.fsproj -c Release -p:Platform=x64`
