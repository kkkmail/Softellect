# Phase 4: Repeater Implementation Plan

## Context

Phases 0-3 implemented direct LAN VNC connections with RSA auth and AES encryption. The viewer connects directly to the service's WCF and UDP endpoints. This only works when both machines are on the same LAN (no NAT traversal).

Phase 4 adds a **repeater** (relay server) that both VNC Service and Viewer connect to **outbound**, enabling NAT traversal. The repeater:
- Maintains a registry of online machines
- Pairs viewers with services on connect
- Relays WCF control traffic (input events, clipboard, etc.)
- Relays UDP frame data (encrypted end-to-end, repeater never decrypts)

## Implementation Steps

### 4.1 — Add Repeater Types to Core/Primitives.fs

**File:** `Vnc/Core/Primitives.fs`

Add types needed for repeater communication:

```fsharp
type VncRegisterRequest =
    {
        machineName : VncMachineName
        machineId : VncMachineId
        serviceUdpPort : int  // port the service's UDP relay endpoint will use
    }

type VncRegisterResponse =
    {
        repeaterUdpPort : int  // port the repeater allocated for this machine's UDP relay
    }

type VncRepeaterConnectRequest =
    {
        viewerId : VncViewerId
        machineName : VncMachineName
        viewerUdpPort : int
    }

type VncRepeaterConnectResponse =
    {
        serviceAccessInfo : ServiceAccessInfo  // WCF endpoint to the actual service (relayed)
        repeaterUdpPort : int  // UDP port on repeater to send/receive frames
        sessionAesKey : byte[]  // NOT set here — the viewer still does RSA auth with service
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

**Key insight:** The repeater does NOT handle auth/encryption. It just relays opaque `byte[]` WCF calls and UDP packets. Auth remains end-to-end between viewer and service.

### 4.2 — Update Core/ServiceInfo.fs

**File:** `Vnc/Core/ServiceInfo.fs`

- `IVncRepeaterService` interface already exists. Update it:
  - `registerMachine : VncRegisterRequest -> VncUnitResult` (service calls this)
  - `unregisterMachine : VncMachineId -> VncUnitResult`
  - `requestConnection : VncRepeaterConnectRequest -> VncResult<VncRepeaterConnectResponse>`
  - `queryStatus : VncStatusRequest -> VncResult<VncStatusResponse>`

- `IVncRepeaterWcfService` already exists with `registerMachine`, `requestConnection`, `queryStatus` methods. Add `unregisterMachine`:
  ```fsharp
  [<OperationContract(Name = "unregisterMachine")>]
  abstract unregisterMachine : data:byte[] -> byte[]
  ```

- Add `VncRepeaterData`:
  ```fsharp
  type VncRepeaterData =
      {
          repeaterAccessInfo : VncRepeaterAccessInfo
          udpRelayBasePort : int  // starting port for UDP relay allocation
      }
  ```

- Add default repeater port constants to `Primitives.fs`:
  ```fsharp
  [<Literal>]
  let DefaultVncRepeaterWcfPort = 5095

  [<Literal>]
  let DefaultVncRepeaterUdpBasePort = 6000
  ```

### 4.3 — Update Core/AppSettings.fs

**File:** `Vnc/Core/AppSettings.fs`

Add config loading for repeater:
```fsharp
let vncRepeaterAccessInfoKey = ConfigKey "VncRepeaterAccessInfo"
let vncRepeaterUdpBasePortKey = ConfigKey "VncRepeaterUdpBasePort"

let loadVncRepeaterAccessInfo () : VncRepeaterAccessInfo = ...
let loadVncRepeaterData () : VncRepeaterData = ...
```

### 4.4 — Create Vnc/Repeater/Repeater.fsproj

**New file:** `Vnc/Repeater/Repeater.fsproj`

- `OutputType`: Exe
- `TargetFramework`: net10.0 (NO `-windows`, cross-platform)
- References: Sys, Wcf, Transport, Core (NO Interop — no Win32 deps)
- Pattern: follows `Vnc/Service/Service.fsproj` structure

### 4.5 — Create Vnc/Repeater/MachineRegistry.fs

**New file:** `Vnc/Repeater/MachineRegistry.fs`

In-memory concurrent machine registry:

```fsharp
module MachineRegistry

type RegisteredMachine =
    {
        machineName : VncMachineName
        machineId : VncMachineId
        registeredAt : DateTime
        mutable lastSeen : DateTime
        serviceUdpPort : int
        relayUdpPort : int  // allocated by repeater
    }

type MachineRegistry() =
    // ConcurrentDictionary<VncMachineId, RegisteredMachine>
    member _.register (req: VncRegisterRequest) (relayPort: int) : unit
    member _.unregister (machineId: VncMachineId) : unit
    member _.tryFind (machineName: VncMachineName) : RegisteredMachine option
    member _.getStatus (names: VncMachineName list) : VncMachineInfo list
    member _.heartbeat (machineId: VncMachineId) : unit
    member _.cleanupStale (timeout: TimeSpan) : unit
```

### 4.6 — Create Vnc/Repeater/UdpRelay.fs

**New file:** `Vnc/Repeater/UdpRelay.fs`

Simple packet-forwarding relay. For each registered machine, allocates a UDP port. When a viewer connects, pairs them:

```fsharp
module UdpRelay

type UdpRelaySession =
    {
        serviceEndpoint : IPEndPoint   // where to forward viewer→service packets
        viewerEndpoint : IPEndPoint    // where to forward service→viewer packets
        relayPort : int                // the port this relay listens on
        mutable udpClient : UdpClient
    }

/// Manages UDP relay ports. Packets arriving from service get forwarded to viewer and vice versa.
/// NO decryption — end-to-end encrypted with session AES key.
type UdpRelayManager(basePort: int) =
    member _.allocatePort () : int
    member _.startRelay (relayPort: int) (serviceEp: IPEndPoint) : unit
    member _.pairViewer (relayPort: int) (viewerEp: IPEndPoint) : unit
    member _.stopRelay (relayPort: int) : unit
```

Each relay port runs a receive loop on a background thread. Packets from service address → forward to viewer. Packets from viewer address → forward to service.

### 4.7 — Create Vnc/Repeater/RepeaterService.fs

**New file:** `Vnc/Repeater/RepeaterService.fs`

Implements `IVncRepeaterService`:

```fsharp
type RepeaterService(data: VncRepeaterData, registry: MachineRegistry, relayManager: UdpRelayManager) =
    interface IVncRepeaterService with
        member _.registerMachine req =
            let relayPort = relayManager.allocatePort()
            registry.register req relayPort
            relayManager.startRelay relayPort (...)
            Ok ()

        member _.unregisterMachine machineId =
            registry.unregister machineId
            Ok ()

        member _.requestConnection req =
            match registry.tryFind req.machineName with
            | Some machine ->
                relayManager.pairViewer machine.relayUdpPort viewerEp
                Ok { repeaterUdpPort = machine.relayUdpPort; ... }
            | None -> Error (VncConnectionErr (MachineOfflineErr req.machineName))

        member _.queryStatus req =
            Ok { machines = registry.getStatus req.machineNames }
```

Also implements `IHostedService` (required by `wcfMain` constraint):
- `StartAsync`: start stale-cleanup timer
- `StopAsync`: stop timer, cleanup relays

### 4.8 — Create Vnc/Repeater/WcfRepeater.fs

**New file:** `Vnc/Repeater/WcfRepeater.fs`

WCF service that handles repeater WCF contract. Follows the `tryReply` pattern from `Wcf/Service.fs`:

```fsharp
[<ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)>]
type VncRepeaterWcfService(service: RepeaterService) =
    let toErr (e: WcfError) : VncError = VncWcfErr (VncRepeaterWcfErr e)

    interface IVncRepeaterWcfService with
        member _.registerMachine data = tryReply (service :> IVncRepeaterService).registerMachine toErr data
        member _.unregisterMachine data = tryReply (service :> IVncRepeaterService).unregisterMachine toErr data
        member _.requestConnection data = tryReply (service :> IVncRepeaterService).requestConnection toErr data
        member _.queryStatus data = tryReply (service :> IVncRepeaterService).queryStatus toErr data
```

Plus `getRepeaterWcfProgram` function following `WcfServer.getVncWcfProgram` pattern.

### 4.9 — Create Vnc/Repeater/Program.fs

**New file:** `Vnc/Repeater/Program.fs`

Follows VNC Service `Program.fs` pattern:

```fsharp
let repeaterMain argv =
    setLogLevel()
    let data = loadVncRepeaterData()
    let registry = MachineRegistry()
    let relayManager = UdpRelayManager(data.udpRelayBasePort)
    let getService () = RepeaterService(data, registry, relayManager)
    let program = getRepeaterWcfProgram data getService argv
    program()
```

### 4.10 — Update VNC Service to Register with Repeater

**Files:** `Vnc/Core/ServiceInfo.fs`, `Vnc/Service/VncService.fs`, `Vnc/Service/Program.fs`

- Add `VncServerData.repeaterAccessInfo : VncRepeaterAccessInfo option`
- In `VncService.StartAsync`: if repeater configured, make WCF call to register machine
- In `VncService.StopAsync`: unregister from repeater
- Add periodic heartbeat (every 30s) to keep registration alive
- Use `tryCommunicate` + `tryGetWcfService<IVncRepeaterWcfService>` to call repeater

The service still hosts its own WCF endpoint (for direct LAN connections). When connecting via repeater, the viewer's WCF calls are relayed through the repeater to the service's WCF endpoint.

**Important:** The repeater relays WCF at the TCP level, NOT at the application level. So the VNC Service's existing WCF endpoint handles all calls — the repeater just forwards raw bytes. This means we need the repeater to act as a WCF proxy/relay for the service's `IVncWcfService` contract too.

**Revised approach — WCF relay:** The repeater needs to relay WCF calls for `IVncWcfService` too (connect, disconnect, sendInput, etc.). Two options:
1. **TCP-level relay** — complex, requires custom proxy
2. **Application-level relay** — repeater implements `IVncWcfService`, forwards each call to the registered service

Option 2 is simpler and fits the existing patterns:

- Repeater exposes `IVncWcfService` on a per-machine basis (or with a session routing header)
- Viewer connects to repeater's `IVncWcfService`
- Repeater forwards opaque `byte[]` to the actual service's `IVncWcfService`

**Simplest approach:** The VNC Service registers with repeater via `IVncRepeaterWcfService`. When viewer requests connection, repeater returns the service's direct WCF URL + allocated UDP relay port. The viewer then:
- Makes WCF calls directly to the service (if reachable — this is the LAN case)
- OR if behind NAT, the service could expose its WCF on the repeater's port via a relay

For Phase 4 MVP, let's use the simplest approach that works: **the repeater relays both UDP and WCF**.

### Revised Architecture: WCF Relay

The repeater will host **two** WCF service contracts:
1. `IVncRepeaterWcfService` — for registration, connection requests, status queries
2. `IVncWcfService` — relayed calls (viewer calls this, repeater forwards to service)

For the relay of `IVncWcfService`, the repeater acts as a **WCF client** to the service and a **WCF server** to the viewer. Each viewer's call is:
1. Viewer → repeater (WCF call with session routing)
2. Repeater → service (WCF call forwarding opaque bytes)

But `wcfMain` only supports hosting a single WCF service interface. So we need to either:
- Host two separate WCF endpoints (two ports)
- Or combine into one interface

**Decision:** Use two ports — the repeater runs `IVncRepeaterWcfService` on its main port, and for each connected service, it also establishes a client WCF connection to the service's `IVncWcfService`. The viewer connects to the **service's WCF** via the repeater's WCF relay port, which is just a forwarding proxy.

Actually, the simplest approach for Phase 4: **the viewer still connects directly to the service's WCF endpoint**. The repeater only relays **UDP frames**. For WCF control traffic:
- Viewer asks repeater for the service's WCF address
- Viewer connects directly to service WCF (works if service's WCF port is open/forwarded)
- Repeater only handles UDP relay (NAT traversal for the high-bandwidth frame data)

This is the simplest Phase 4 that works for many real-world scenarios (WCF port-forwarded, UDP behind NAT).

### Final Simplified Architecture

1. **Repeater** hosts `IVncRepeaterWcfService` (registration, connection, status)
2. **Repeater** runs UDP relay (forwards frames between service and viewer)
3. **Service** registers with repeater on startup, sends UDP frames to repeater relay port
4. **Viewer** asks repeater for machine info, gets service's WCF URL + repeater UDP port
5. **Viewer** connects to service WCF directly, receives frames via repeater UDP relay

This keeps all existing WCF auth/encryption working unchanged. Only the UDP path changes to go through the relay.

---

## Detailed File Changes

### New Files

| # | File | Description |
|---|------|-------------|
| 1 | `Vnc/Repeater/Repeater.fsproj` | Project file (Exe, net10.0, no Win32) |
| 2 | `Vnc/Repeater/MachineRegistry.fs` | In-memory machine registry |
| 3 | `Vnc/Repeater/UdpRelay.fs` | UDP packet forwarding relay |
| 4 | `Vnc/Repeater/RepeaterService.fs` | IVncRepeaterService + IHostedService impl |
| 5 | `Vnc/Repeater/WcfRepeater.fs` | WCF service + wcfMain wrapper |
| 6 | `Vnc/Repeater/Program.fs` | Entry point |

### Modified Files

| # | File | Changes |
|---|------|---------|
| 7 | `Vnc/Core/Primitives.fs` | Add repeater request/response types, default ports |
| 8 | `Vnc/Core/ServiceInfo.fs` | Update IVncRepeaterService, IVncRepeaterWcfService, add VncRepeaterData, update VncServerData |
| 9 | `Vnc/Core/AppSettings.fs` | Add repeater config loading |
| 10 | `Vnc/Service/VncService.fs` | Add repeater registration on StartAsync, heartbeat, unregister on StopAsync |
| 11 | `Vnc/Service/CaptureService.fs` | Support sending UDP to repeater relay port (instead of directly to viewer) |
| 12 | `Vnc/Service/Program.fs` | Load repeater config, pass to VncServerData |
| 13 | `Vnc/Viewer/Program.fs` | Add `--repeater` CLI arg for repeater address |
| 14 | `Vnc/Viewer/ViewerForm.fs` | Query repeater for machine info before connecting, use relay UDP port |
| 15 | `SoftellectMain.slnx` | Add Vnc/Repeater project |

### Key Patterns to Reuse

- **`Wcf/Program.fs:wcfMain`** — for repeater WCF hosting (`ProgramData<IVncRepeaterService, VncRepeaterWcfService>`)
- **`Wcf/Service.fs:tryReply`** — for repeater WCF handler deserialization
- **`Wcf/Client.fs:tryCommunicate`** — for service→repeater and viewer→repeater WCF calls
- **`Wcf/Client.fs:tryGetWcfService`** — for creating WCF client channels
- **`Vnc/Service/WcfServer.fs:getVncWcfProgram`** — template for `getRepeaterWcfProgram`
- **`Vnc/Core/CryptoHelpers.fs`** — NOT used by repeater (end-to-end encrypted)
- **`Transport/UdpProtocol.fs`** — UDP datagram format stays the same

## Implementation Order

1. `Vnc/Core/Primitives.fs` — add types + constants
2. `Vnc/Core/ServiceInfo.fs` — update interfaces + add VncRepeaterData
3. `Vnc/Core/AppSettings.fs` — add config loading
4. `Vnc/Repeater/Repeater.fsproj` — create project
5. `Vnc/Repeater/MachineRegistry.fs` — machine registry
6. `Vnc/Repeater/UdpRelay.fs` — UDP relay
7. `Vnc/Repeater/RepeaterService.fs` — service implementation
8. `Vnc/Repeater/WcfRepeater.fs` — WCF layer + program wrapper
9. `Vnc/Repeater/Program.fs` — entry point
10. `SoftellectMain.slnx` — add project
11. Build & fix errors
12. `Vnc/Service/VncService.fs` — add repeater registration
13. `Vnc/Service/CaptureService.fs` — send UDP to relay
14. `Vnc/Service/Program.fs` — load repeater config
15. `Vnc/Viewer/ViewerForm.fs` — query repeater, use relay port
16. `Vnc/Viewer/Program.fs` — add CLI args
17. Build & fix errors

## Verification

1. **Build:** `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SoftellectMain.slnx -p:Configuration=Release -p:Platform=x64` — 0 errors
2. **Unit tests:** `dotnet test TransportTests/TransportTests.fsproj -c Release -p:Platform=x64 --no-build` and `dotnet test SysTests/SysTests.fsproj -c Release -p:Platform=x64 --no-build` — all pass
3. **Manual test scenario:**
   - Start VNC Repeater on a machine with open ports
   - Start VNC Service with `--repeater <repeater-address>:<port>`
   - Start VNC Viewer with `--repeater <repeater-address>:<port> --machine <machine-name>`
   - Verify: service registers, viewer queries status, viewer connects, frames relay through repeater
