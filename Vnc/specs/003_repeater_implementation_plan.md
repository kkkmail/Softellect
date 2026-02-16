# Phase 4: Repeater Implementation Plan

## Context

Phases 0-3 built a working VNC system with direct LAN connections, RSA auth, and per-packet AES encryption. The viewer connects directly to the service's WCF and UDP endpoints. Phase 4 adds a **repeater** (relay) so that:

- The viewer connects to the repeater's WCF and UDP endpoints
- The repeater forwards WCF calls to the actual service and relays UDP frames
- The repeater verifies request signatures using pre-shared public keys but NEVER decrypts content
- All machine/viewer/key configuration is static in `appsettings.json` — no dynamic registration

## Architecture

```
Viewer ──WCF──▶ Repeater ──WCF──▶ Service
Viewer ◀──UDP──▶ Repeater ◀──UDP──▶ Service
```

- **Single WCF contract** (`IVncRepeaterWcfService`) with same methods as `IVncWcfService` plus `queryStatus`
- **Single port** (default 45003) for both WCF (TCP) and UDP
- **Repeater is dumb:** verifies signatures, forwards opaque bytes, does IP-matching for UDP
- **No dynamic registration:** all machines, viewers, keys, and addresses are in appsettings

### WCF Flow

1. Viewer wraps encrypted payload in a `VncRelayEnvelope` (adds routing info + outer signature)
2. Viewer calls repeater's `IVncRepeaterWcfService.connect(envelope_bytes)`
3. Repeater deserializes envelope, verifies outer signature with viewer's pre-shared public key
4. Repeater looks up target machine's WCF address from appsettings
5. Repeater creates WCF client to service, calls `IVncWcfService.connect(payload)` with the inner payload
6. Service processes (decrypts, verifies inner RSA auth) and returns encrypted response
7. Repeater wraps response bytes in a response envelope and returns to viewer

### UDP Flow

1. Service sends UDP frames to repeater's UDP address (configured in service appsettings)
2. Repeater receives packet, matches source IP to a known machine (from appsettings)
3. Repeater forwards raw packet to the paired viewer's IP:port (learned from first incoming viewer UDP packet for that session, matched by IP)
4. Viewer sends UDP keepalives/acks to repeater to establish its IP:port mapping

### Envelope Format

```fsharp
type VncRelayEnvelope =
    {
        senderId : Guid              // ViewerId or MachineId
        targetMachineId : VncMachineId
        methodName : string          // "connect", "disconnect", etc.
        payloadHash : byte[]         // SHA256 of payload (for signature verification without copying)
        signature : byte[]           // signData(senderId + targetMachineId + methodName + payloadHash, senderPrivateKey)
        payload : byte[]             // opaque encrypted bytes (forwarded verbatim)
    }
```

The repeater:
1. Deserializes envelope with `tryDeserialize wcfSerializationFormat`
2. Reconstructs signed data: `senderId + targetMachineId + methodName + payloadHash`
3. Calls `verifySignature signedData envelope.signature senderPublicKey` (from `Sys/Crypto.fs`)
4. Verifies `SHA256.HashData(payload) = payloadHash`
5. Forwards `payload` to target service's WCF method

### Signature Functions (from Sys/Crypto.fs)

- `signData (data: byte[]) (privateKey: PrivateKey) : Result<byte[], SysError>` — RSA signs data, returns signature bytes
- `verifySignature (data: byte[]) (signature: byte[]) (publicKey: PublicKey) : Result<unit, SysError>` — verifies RSA signature

---

## Configuration (appsettings.json)

### Repeater appsettings

```json
{
  "appSettings": {
    "VncRepeaterAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=0.0.0.0;netTcpServicePort=45003;netTcpServiceName=VncRepeaterService;netTcpSecurityMode=NoSecurity",
    "VncRepeaterUdpPort": "45003",
    "VncRepeaterMachines": [
      {
        "MachineName": "WorkPC",
        "MachineId": "{guid}",
        "ServiceWcfAddress": "NetTcpServiceInfo|netTcpServiceAddress=192.168.1.100;netTcpServicePort=5090;netTcpServiceName=VncService;netTcpSecurityMode=NoSecurity",
        "ServicePublicKeyPath": "Keys/Machines/workpc.pkx"
      }
    ],
    "VncRepeaterViewers": [
      {
        "ViewerId": "{guid}",
        "ViewerPublicKeyPath": "Keys/Viewers/{guid}.pkx",
        "AllowedMachines": ["WorkPC"]
      }
    ]
  }
}
```

### Service appsettings (with repeater)

```json
{
  "appSettings": {
    "VncRepeaterUdpAddress": "repeater.example.com",
    "VncRepeaterUdpPort": "45003"
  }
}
```

When `VncRepeaterUdpAddress` is set, the service sends UDP frames to the repeater instead of directly to the viewer.

### Viewer appsettings / CLI args

```
--repeater repeater.example.com:45003 --machine WorkPC
```

When `--repeater` is set, the viewer wraps WCF calls in envelopes and sends to the repeater, and receives UDP from the repeater.

---

## Detailed File Changes

### Step 1: Update Core/Primitives.fs

**File:** `Vnc/Core/Primitives.fs`

Add:

```fsharp
[<Literal>]
let DefaultVncRepeaterPort = 45003

type VncRelayEnvelope =
    {
        senderId : Guid
        targetMachineId : VncMachineId
        methodName : string
        payloadHash : byte[]
        signature : byte[]
        payload : byte[]
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

### Step 2: Update Core/ServiceInfo.fs

**File:** `Vnc/Core/ServiceInfo.fs`

Update `IVncRepeaterWcfService` — same methods as `IVncWcfService` plus `queryStatus`:

```fsharp
[<ServiceContract(ConfigurationName = VncRepeaterServiceName)>]
type IVncRepeaterWcfService =
    [<OperationContract(Name = "connect")>]
    abstract connect : data:byte[] -> byte[]

    [<OperationContract(Name = "disconnect")>]
    abstract disconnect : data:byte[] -> byte[]

    [<OperationContract(Name = "sendInput")>]
    abstract sendInput : data:byte[] -> byte[]

    [<OperationContract(Name = "getClipboard")>]
    abstract getClipboard : data:byte[] -> byte[]

    [<OperationContract(Name = "setClipboard")>]
    abstract setClipboard : data:byte[] -> byte[]

    [<OperationContract(Name = "listDirectory")>]
    abstract listDirectory : data:byte[] -> byte[]

    [<OperationContract(Name = "readFileChunk")>]
    abstract readFileChunk : data:byte[] -> byte[]

    [<OperationContract(Name = "writeFileChunk")>]
    abstract writeFileChunk : data:byte[] -> byte[]

    [<OperationContract(Name = "queryStatus")>]
    abstract queryStatus : data:byte[] -> byte[]
```

Update `IVncRepeaterService`:

```fsharp
type IVncRepeaterService =
    inherit IHostedService
    abstract relayCall : methodName:string -> VncRelayEnvelope -> VncResult<byte[]>
    abstract queryStatus : VncStatusRequest -> VncResult<VncStatusResponse>
```

Add `VncRepeaterData`:

```fsharp
type VncRepeaterMachineConfig =
    {
        machineName : VncMachineName
        machineId : VncMachineId
        serviceAccessInfo : ServiceAccessInfo
        servicePublicKey : PublicKey
    }

type VncRepeaterViewerConfig =
    {
        viewerId : VncViewerId
        viewerPublicKey : PublicKey
        allowedMachines : VncMachineName list
    }

type VncRepeaterData =
    {
        repeaterAccessInfo : VncRepeaterAccessInfo
        udpPort : int
        machines : VncRepeaterMachineConfig list
        viewers : VncRepeaterViewerConfig list
    }
```

Update `VncServerData` — add optional repeater UDP target:

```fsharp
type VncServerData =
    {
        vncServiceAccessInfo : VncServiceAccessInfo
        serverPrivateKey : PrivateKey
        serverPublicKey : PublicKey
        viewerKeysPath : FolderName
        encryptionType : EncryptionType
        repeaterUdpTarget : (string * int) option  // if set, send UDP to repeater instead of viewer
    }
```

### Step 3: Update Core/AppSettings.fs

**File:** `Vnc/Core/AppSettings.fs`

Add:

```fsharp
let vncRepeaterUdpAddressKey = ConfigKey "VncRepeaterUdpAddress"
let vncRepeaterUdpPortKey = ConfigKey "VncRepeaterUdpPort"

let loadVncRepeaterUdpTarget () : (string * int) option =
    // Returns Some (address, port) if VncRepeaterUdpAddress is configured
    ...

let loadVncRepeaterData () : VncRepeaterData =
    // Loads repeater config: machines, viewers, keys
    ...
```

### Step 4: Create Vnc/Repeater/Repeater.fsproj

**New file.** Exe project, `net10.0` (NO `-windows`), references: Sys, Wcf, Transport, Core. NO Interop reference.

### Step 5: Create Vnc/Repeater/UdpRelay.fs

**New file.** UDP relay using IP-IP matching.

```fsharp
module UdpRelay

type UdpRelayState =
    {
        // Map from machine source IP → (viewer IP:port) learned from first viewer packet
        // Map from viewer source IP → (machine IP:port) from config
        machineToViewer : ConcurrentDictionary<IPAddress, IPEndPoint>
        viewerToMachine : ConcurrentDictionary<IPAddress, IPEndPoint>
    }
```

The relay loop:
1. Receives UDP packet on the single UDP port
2. Checks source IP against known machines (from appsettings)
   - If from a known machine → forward to paired viewer's IP:port (if known)
3. Checks source IP against known viewer sessions
   - If from a viewer → forward to the paired machine's IP:port
4. Unknown source IPs are dropped

Viewer IP:port is learned dynamically from the first UDP packet the viewer sends (a keepalive/probe). The machine IP is configured in appsettings OR learned from the machine's first UDP packet.

### Step 6: Create Vnc/Repeater/RepeaterService.fs

**New file.** Implements `IVncRepeaterService`.

```fsharp
type RepeaterService(data: VncRepeaterData, relay: UdpRelayManager) =
    // Lookup tables built from appsettings
    let machinesByName : Map<VncMachineName, VncRepeaterMachineConfig>
    let machinesById : Map<VncMachineId, VncRepeaterMachineConfig>
    let viewersById : Map<VncViewerId, VncRepeaterViewerConfig>

    // Verify envelope signature using sender's public key
    let verifyEnvelope (envelope: VncRelayEnvelope) : Result<VncRepeaterMachineConfig, VncError>

    // Forward a WCF call to the target service
    let forwardToService (machine: VncRepeaterMachineConfig) (methodName: string) (payload: byte[]) : Result<byte[], VncError>

    interface IVncRepeaterService with
        member _.relayCall methodName envelope = ...
        member _.queryStatus request = ...

    interface IHostedService with
        member _.StartAsync ct = // start UDP relay
        member _.StopAsync ct = // stop UDP relay
```

`forwardToService` uses `tryGetWcfService<IVncWcfService>` and `tryCommunicate` from `Wcf/Client.fs` to call the actual service.

### Step 7: Create Vnc/Repeater/WcfRepeater.fs

**New file.** WCF service layer.

```fsharp
[<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)>]
type VncRepeaterWcfService(service: RepeaterService) =
    let toErr (e: WcfError) : VncError = VncWcfErr (VncRepeaterWcfErr e)

    let relay methodName (data: byte[]) : byte[] =
        // Deserialize envelope, call service.relayCall, serialize response
        tryReply (fun envelope -> service.relayCall methodName envelope) toErr data

    interface IVncRepeaterWcfService with
        member _.connect data = relay "connect" data
        member _.disconnect data = relay "disconnect" data
        member _.sendInput data = relay "sendInput" data
        member _.getClipboard data = relay "getClipboard" data
        member _.setClipboard data = relay "setClipboard" data
        member _.listDirectory data = relay "listDirectory" data
        member _.readFileChunk data = relay "readFileChunk" data
        member _.writeFileChunk data = relay "writeFileChunk" data
        member _.queryStatus data = tryReply (service :> IVncRepeaterService).queryStatus toErr data
```

Plus `getRepeaterWcfProgram` following `WcfServer.getVncWcfProgram` pattern with `wcfMain<IVncRepeaterService, IVncRepeaterWcfService, VncRepeaterWcfService>`.

### Step 8: Create Vnc/Repeater/Program.fs

**New file.** Entry point.

```fsharp
let repeaterMain argv =
    setLogLevel()
    let data = loadVncRepeaterData()
    let relay = UdpRelayManager(data.udpPort, data.machines)
    let getService () = RepeaterService(data, relay)
    let program = getRepeaterWcfProgram data getService argv
    program()
```

### Step 9: Update SoftellectMain.slnx

Add `Vnc/Repeater/Repeater.fsproj` to the `/Vnc/` folder.

### Step 10: Update VNC Service for repeater support

**File:** `Vnc/Service/VncService.fs`

- In `startCapture`: if `data.repeaterUdpTarget` is `Some (host, port)`, use that as the UDP target endpoint instead of `request.viewerUdpAddress:request.viewerUdpPort`

**File:** `Vnc/Service/Program.fs`

- Load `repeaterUdpTarget` from appsettings and pass to `VncServerData`

### Step 11: Update VNC Viewer for repeater support

**File:** `Vnc/Viewer/WcfClient.fs`

Add envelope wrapping to `VncWcfClient`:

- New constructor parameter: `repeaterInfo: (ServiceAccessInfo * VncMachineId) option`
- If repeater configured: wrap each WCF call payload in `VncRelayEnvelope`, sign with viewer's private key, call repeater's `IVncRepeaterWcfService`
- If not configured: call `IVncWcfService` directly (current behavior)

The signing:
```fsharp
let createEnvelope (viewerData: VncViewerData) (machineId: VncMachineId) (methodName: string) (payload: byte[]) =
    let payloadHash = SHA256.HashData(payload)
    let sigData = Array.concat [
        viewerData.viewerId.value.ToByteArray()
        machineId.value.ToByteArray()
        Text.Encoding.UTF8.GetBytes(methodName)
        payloadHash
    ]
    match signData sigData viewerData.viewerPrivateKey with
    | Ok signature ->
        Ok {
            senderId = viewerData.viewerId.value
            targetMachineId = machineId
            methodName = methodName
            payloadHash = payloadHash
            signature = signature
            payload = payload
        }
    | Error e -> Error (VncGeneralErr $"Failed to sign relay envelope: %A{e}")
```

**File:** `Vnc/Viewer/Program.fs`

- Add `--repeater <host:port>` and `--machine <name>` CLI args
- When repeater is specified, pass repeater info to `VncWcfClient`

**File:** `Vnc/Viewer/ViewerForm.fs`

- When using repeater, send initial UDP keepalive to repeater so it learns viewer's IP:port
- Receive UDP frames from repeater instead of from service

---

## Implementation Order

1. `Vnc/Core/Primitives.fs` — add types + constants
2. `Vnc/Core/ServiceInfo.fs` — update interfaces, add VncRepeaterData, update VncServerData
3. `Vnc/Core/AppSettings.fs` — add config loading
4. `Vnc/Repeater/Repeater.fsproj` — create project
5. `Vnc/Repeater/UdpRelay.fs` — UDP relay with IP matching
6. `Vnc/Repeater/RepeaterService.fs` — service implementation
7. `Vnc/Repeater/WcfRepeater.fs` — WCF layer + program wrapper
8. `Vnc/Repeater/Program.fs` — entry point
9. `SoftellectMain.slnx` — add project
10. Build & fix errors
11. `Vnc/Service/VncService.fs` — use repeater UDP target
12. `Vnc/Service/Program.fs` — load repeater config
13. `Vnc/Viewer/WcfClient.fs` — add envelope wrapping for repeater mode
14. `Vnc/Viewer/ViewerForm.fs` — UDP keepalive for repeater
15. `Vnc/Viewer/Program.fs` — add CLI args
16. Build & fix errors

## Key Patterns to Reuse

| Pattern | Source File | Usage |
|---------|------------|-------|
| `wcfMain` | `Wcf/Program.fs` | Repeater WCF hosting |
| `tryReply` | `Wcf/Service.fs` | Repeater WCF handler |
| `tryCommunicate` | `Wcf/Client.fs` | Repeater → Service WCF forwarding |
| `tryGetWcfService` | `Wcf/Client.fs` | Create WCF client to service |
| `signData` | `Sys/Crypto.fs` | Viewer signs envelope |
| `verifySignature` | `Sys/Crypto.fs` | Repeater verifies envelope |
| `getVncWcfProgram` | `Vnc/Service/WcfServer.fs` | Template for `getRepeaterWcfProgram` |
| `VncServiceAccessInfo` | `Vnc/Core/ServiceInfo.fs` | Config pattern |
| `loadVncServiceAccessInfo` | `Vnc/Core/AppSettings.fs` | Config loading pattern |

## Verification

1. **Build:** `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SoftellectMain.slnx -p:Configuration=Release -p:Platform=x64` — 0 errors
2. **Tests:** `dotnet test TransportTests/TransportTests.fsproj -c Release -p:Platform=x64 --no-build` and `dotnet test SysTests/SysTests.fsproj -c Release -p:Platform=x64 --no-build` — all pass
3. **Manual test:**
   - Start VNC Repeater with configured machines/viewers in appsettings
   - Start VNC Service with `VncRepeaterUdpAddress` pointing to repeater
   - Start VNC Viewer with `--repeater <addr>:45003 --machine WorkPC`
   - Verify: viewer connects through repeater, frames relay through repeater UDP
