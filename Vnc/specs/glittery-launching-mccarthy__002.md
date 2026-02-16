# Phase 4: Repeater Implementation Plan (Revised)

## Context

Phases 0-3 built direct LAN VNC with RSA auth and per-packet AES encryption. Phase 4 adds a repeater (relay) following the established VPN patterns from `Vpn/Server/`. The repeater is "dumber than a cork" — it verifies signatures using pre-shared public keys, forwards opaque bytes, and relays UDP via IP matching. All allowed machines/viewers are defined by having key files in configured folders — no dynamic adding.

## Architecture

```
Service ──WCF register──▶ Repeater         (machine announces its current IP)
Viewer  ──WCF connect───▶ Repeater ──WCF──▶ Service  (relay WCF calls)
Service ──UDP frames────▶ Repeater ──UDP──▶ Viewer   (relay frames)
Viewer  ──UDP keepalive─▶ Repeater          (learn viewer's UDP address)
```

- **Single port** (default 45003) for WCF and UDP
- **Single WCF contract** (`IVncRepeaterWcfService`) with register + all `IVncWcfService` methods + queryStatus
- **Single-instance WCF service** (not per-call)
- **IPs are dynamic** — learned at runtime via registration & keepalive (like VPN `currentEndpoint`)
- **No C# mutable structures** — use F# `IpAddress * int` instead of `IPEndPoint`
- **Relay packet format** extracted to `Transport/RelayProtocol.fs`

## Relay Packet Format (extracted to Transport)

Following VPN `tryDecryptAndVerifyRequest` pattern where [clientId(16 bytes)] is prefix:

```
Wire format: [senderId(16)] [command(1)] [targetId(16)] [payload(N)] [signature(S)]
```

- `senderId`: GUID of sender (viewerId or machineId)
- `command`: 1 byte method identifier
- `targetId`: GUID of target machine (for routing)
- `payload`: opaque bytes (encrypted WCF payload or serialized registration data)
- `signature`: RSA signature over [senderId + command + targetId + payload]

Verification uses `tryVerify` from `Sys/Crypto.fs` which splits [data | signature] based on RSA key size.

**Command bytes:**
```fsharp
[<Literal>] let RelayCmdRegister = 1uy     // Machine registers its current WCF address
[<Literal>] let RelayCmdConnect = 2uy       // Viewer connect
[<Literal>] let RelayCmdDisconnect = 3uy
[<Literal>] let RelayCmdSendInput = 4uy
[<Literal>] let RelayCmdGetClipboard = 5uy
[<Literal>] let RelayCmdSetClipboard = 6uy
[<Literal>] let RelayCmdListDirectory = 7uy
[<Literal>] let RelayCmdReadFileChunk = 8uy
[<Literal>] let RelayCmdWriteFileChunk = 9uy
[<Literal>] let RelayCmdQueryStatus = 10uy
```

## Flows

### Machine Registration (like VPN authenticate)

1. VNC Service starts, if repeater configured, calls `repeater.register(data)`
2. `data = [machineId | RelayCmdRegister | machineId | serialize(serviceAccessInfo) | signature]`
3. Repeater: extract machineId → load machine public key from `MachineKeysPath/{machineId}.pkx` → `tryVerify` → deserialize `ServiceAccessInfo`
4. Repeater stores machine's current WCF address + source IP in session
5. Machine re-registers every 30s as keep-alive (IP refresh)

### Viewer WCF Calls (relayed)

1. Viewer calls `repeater.connect(data)` with relay packet
2. `data = [viewerId | RelayCmdConnect | targetMachineId | encryptedConnectPayload | signature]`
3. Repeater: extract viewerId → load viewer public key from `ViewerKeysPath/{viewerId}.pkx` → `tryVerify` → extract command + targetMachineId + encryptedPayload
4. Repeater looks up machine session → gets machine's WCF `ServiceAccessInfo`
5. Repeater calls `tryGetWcfService<IVncWcfService>` to machine's WCF, forwards encryptedPayload
6. Returns machine's response to viewer
7. Subsequent calls (sendInput, etc.) same pattern — session tracks viewer→machine pairing

### UDP Relay (IP matching like VPN `updatePushEndpoint`)

1. Machine sends UDP frames to repeater port — repeater matches source IP to registered machine
2. Viewer sends UDP keepalive to repeater port — repeater matches source IP to connected viewer
3. Relay: machine packets → forward to viewer's learned IP:port, viewer packets → forward to machine's learned IP:port

## Configuration (appsettings.json)

### Repeater — follow VPN Server pattern (`Apps/Vpn/VpnServer/appsettings.json`)
```json
{
  "appSettings": {
    "VncRepeaterAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=0.0.0.0;netTcpServicePort=45003;netTcpServiceName=VncRepeaterService;netTcpSecurityMode=NoSecurity",
    "MachineKeysPath": "C:\\Keys\\Machines",
    "ViewerKeysPath": "C:\\Keys\\Viewers"
  }
}
```
Machines allowed = key files exist in `MachineKeysPath`. Viewers allowed = key files exist in `ViewerKeysPath`.

### VNC Service (with repeater)
```json
{
  "appSettings": {
    "VncRepeaterAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=repeater.example.com;netTcpServicePort=45003;netTcpServiceName=VncRepeaterService;netTcpSecurityMode=NoSecurity"
  }
}
```

### VNC Viewer (with repeater)
```json
{
  "appSettings": {
    "VncRepeaterAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=repeater.example.com;netTcpServicePort=45003;netTcpServiceName=VncRepeaterService;netTcpSecurityMode=NoSecurity",
    "VncTargetMachineId": "{guid}"
  }
}
```

No CLI args — all from appsettings + keys.

---

## Implementation Steps

### Step 1: Add relay protocol to Transport (`Transport/RelayProtocol.fs`)

**New file.** Generic relay packet format — not VNC-specific.

```fsharp
module Softellect.Transport.RelayProtocol

[<Literal>] let RelayClientIdSize = 16
[<Literal>] let RelayCommandSize = 1
[<Literal>] let RelayTargetIdSize = 16
[<Literal>] let RelayHeaderSize = 33  // 16 + 1 + 16

// Command constants
[<Literal>] let RelayCmdRegister = 1uy
[<Literal>] let RelayCmdConnect = 2uy
// ... etc.

// Build relay data (before signing)
let buildRelayData (senderId: Guid) (command: byte) (targetId: Guid) (payload: byte[]) : byte[]

// Parse verified relay data (after tryVerify stripped signature)
let tryParseRelayData (data: byte[]) : Result<Guid * byte * Guid * byte[], string>
```

Update `Transport/Transport.fsproj` compile list to include `RelayProtocol.fs`.

### Step 2: Add types to `Vnc/Core/Primitives.fs`

```fsharp
[<Literal>]
let DefaultVncRepeaterPort = 45003

type UdpEndpoint =
    {
        address : IpAddress
        port : int
    }

type VncStatusRequest =
    {
        machineNames : VncMachineName list
    }

type VncStatusResponse =
    {
        machines : VncMachineInfo list
    }
```

### Step 3: Update `Vnc/Core/ServiceInfo.fs`

Replace `IVncRepeaterWcfService` — register + all IVncWcfService methods + queryStatus:
```fsharp
[<ServiceContract(ConfigurationName = VncRepeaterServiceName)>]
type IVncRepeaterWcfService =
    abstract register : data:byte[] -> byte[]
    abstract connect : data:byte[] -> byte[]
    abstract disconnect : data:byte[] -> byte[]
    abstract sendInput : data:byte[] -> byte[]
    abstract getClipboard : data:byte[] -> byte[]
    abstract setClipboard : data:byte[] -> byte[]
    abstract listDirectory : data:byte[] -> byte[]
    abstract readFileChunk : data:byte[] -> byte[]
    abstract writeFileChunk : data:byte[] -> byte[]
    abstract queryStatus : data:byte[] -> byte[]
```

Replace `IVncRepeaterService`:
```fsharp
type IVncRepeaterService =
    inherit IHostedService
    abstract handleRelayCall : byte[] -> byte -> VncResult<byte[]>
    abstract queryStatus : VncStatusRequest -> VncResult<VncStatusResponse>
```

Add `VncRepeaterData`:
```fsharp
type VncRepeaterData =
    {
        repeaterAccessInfo : VncRepeaterAccessInfo
        machineKeysPath : FolderName
        viewerKeysPath : FolderName
    }
```

Update `VncServerData` — add optional repeater:
```fsharp
type VncServerData =
    {
        // ... existing fields ...
        repeaterAccessInfo : VncRepeaterAccessInfo option  // if set, register with repeater
    }
```

Update `VncViewerData` — add optional repeater + target:
```fsharp
type VncViewerData =
    {
        // ... existing fields ...
        repeaterAccessInfo : VncRepeaterAccessInfo option
        targetMachineId : VncMachineId option
    }
```

### Step 4: Update `Vnc/Core/AppSettings.fs`

Add config loading for repeater:
```fsharp
let loadVncRepeaterAccessInfoOpt () : VncRepeaterAccessInfo option
let loadVncRepeaterData () : VncRepeaterData
let loadVncTargetMachineId () : VncMachineId option
```

### Step 5: Create `Vnc/Repeater/Repeater.fsproj`

Exe, `net10.0` (NO `-windows`), references: Sys, Wcf, Transport, Core. NO Interop.

### Step 6: Create `Vnc/Repeater/RelaySession.fs`

Relay session state following VPN `ClientRegistry` pattern:

```fsharp
type RelayMachineSession =
    {
        machineId : VncMachineId
        machinePublicKey : PublicKey
        mutable serviceAccessInfo : ServiceAccessInfo option  // learned from register
        mutable udpEndpoint : UdpEndpoint option              // learned from first UDP
        mutable lastSeen : DateTime
    }

type RelayViewerSession =
    {
        viewerId : VncViewerId
        viewerPublicKey : PublicKey
        targetMachineId : VncMachineId
        mutable udpEndpoint : UdpEndpoint option  // learned from first UDP keepalive
        mutable lastSeen : DateTime
    }

type RelaySessionManager(data: VncRepeaterData) =
    // Load keys from folders on construction
    // ConcurrentDictionary<VncMachineId, RelayMachineSession>
    // ConcurrentDictionary<VncViewerId, RelayViewerSession>
    member _.tryGetMachine : VncMachineId -> RelayMachineSession option
    member _.tryGetViewer : VncViewerId -> RelayViewerSession option
    member _.tryGetMachineByIp : IpAddress -> RelayMachineSession option
    member _.tryGetViewerByIp : IpAddress -> RelayViewerSession option
    member _.registerMachine : VncMachineId -> ServiceAccessInfo -> unit
    member _.pairViewer : VncViewerId -> VncMachineId -> unit
    member _.updateMachineUdp : VncMachineId -> UdpEndpoint -> unit
    member _.updateViewerUdp : VncViewerId -> UdpEndpoint -> unit
```

Keys loaded from folder on startup (like VPN `tryLoadClientPublicKey` pattern: `{id}.pkx` files).

### Step 7: Create `Vnc/Repeater/UdpRelay.fs`

UDP relay with IP matching. Single UDP port, single receive loop.

```fsharp
type UdpRelayManager(port: int, sessions: RelaySessionManager) =
    // Receive loop: match source IP → machine or viewer → forward to paired endpoint
    // NO C# IPEndPoint in public API — convert at boundary
    member _.start : CancellationToken -> unit
    member _.stop : unit -> unit
```

On receive:
1. Get source IP from UDP receive (convert to `IpAddress` at boundary)
2. Check if source matches a known machine → forward to paired viewer
3. Check if source matches a known viewer → forward to paired machine
4. Unknown source → drop

### Step 8: Create `Vnc/Repeater/RepeaterService.fs`

Implements `IVncRepeaterService` + `IHostedService`.

```fsharp
type RepeaterService(data: VncRepeaterData) =
    let sessions = RelaySessionManager(data)
    let relay = UdpRelayManager(port, sessions)

    // For each WCF call:
    // 1. tryVerify to extract [senderId | command | targetId | payload]
    // 2. Match senderId to machine or viewer public key
    // 3. For register: store machine's ServiceAccessInfo
    // 4. For viewer calls: forward payload to machine's WCF using tryCommunicate
    // 5. For queryStatus: build response from session state
```

Uses `tryGetWcfService<IVncWcfService>` + `tryCommunicate` from `Wcf/Client.fs` to forward to actual service.

### Step 9: Create `Vnc/Repeater/WcfRepeater.fs`

**Single-instance** WCF service:
```fsharp
[<ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)>]
type VncRepeaterWcfService(service: RepeaterService) =
```

Each method extracts the command byte and delegates to `service.handleRelayCall`. Plus `getRepeaterWcfProgram` following `WcfServer.getVncWcfProgram` pattern.

### Step 10: Create `Vnc/Repeater/Program.fs`

Entry point — just load config and run.

### Step 11: Update `SoftellectMain.slnx`

Add `Vnc/Repeater/Repeater.fsproj` to `/Vnc/` folder.

### Step 12: Build & fix errors

### Step 13: Update VNC Service for repeater registration

**`Vnc/Service/VncService.fs`:**
- On `StartAsync`: if `repeaterAccessInfo` configured, call repeater's `register` method
- Periodic re-registration every 30s (keep-alive, like VPN ping)
- On `StopAsync`: stop keep-alive

**`Vnc/Service/CaptureService.fs`:**
- If repeater configured, send UDP frames to repeater instead of directly to viewer

**`Vnc/Service/Program.fs`:**
- Load `repeaterAccessInfo` from appsettings, pass to `VncServerData`

### Step 14: Update VNC Viewer for repeater mode

**`Vnc/Viewer/WcfClient.fs`:**
- If repeater configured: wrap each WCF call in relay packet format (buildRelayData + signData), call `IVncRepeaterWcfService` instead of `IVncWcfService`
- If not configured: direct call (current behavior unchanged)

**`Vnc/Viewer/ViewerForm.fs`:**
- If repeater configured: send UDP keepalive to repeater to establish address mapping
- Receive frames from repeater instead of from service

**`Vnc/Viewer/Program.fs`:**
- Load repeater config + target machine ID from appsettings (no CLI args)

### Step 15: Build & fix errors

---

## Key Patterns to Reuse

| Pattern | Source | Usage |
|---------|--------|-------|
| `tryVerify` | `Sys/Crypto.fs` | Repeater verifies relay packets |
| `signData` | `Sys/Crypto.fs` | Viewer/service signs relay packets |
| `tryGetWcfService` | `Wcf/Client.fs` | Repeater creates WCF client to service |
| `tryCommunicate` | `Wcf/Client.fs` | Repeater forwards WCF calls |
| `tryReply` | `Wcf/Service.fs` | Repeater WCF handler |
| `wcfMain` | `Wcf/Program.fs` | Repeater WCF hosting |
| `getVncWcfProgram` | `Vnc/Service/WcfServer.fs` | Template for `getRepeaterWcfProgram` |
| `tryLoadClientPublicKey` | `Vpn/Server/WcfServer.fs` | Pattern for loading keys from folder |
| `updatePushEndpoint` | `Vpn/Server/ClientRegistry.fs` | Pattern for dynamic IP tracking |
| `keepaliveLoop` | `Vpn/Client/UdpClient.fs` | Pattern for UDP keepalive |
| `IpAddress` (F# DU) | `Sys/Primitives.fs` | Use instead of C# IPEndPoint |

## Verification

1. **Build:** `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SoftellectMain.slnx -p:Configuration=Release -p:Platform=x64` — 0 errors
2. **Tests:** existing tests pass
3. **Manual test:** Repeater with keys in folders → Service registers → Viewer connects through repeater → Frames relay
