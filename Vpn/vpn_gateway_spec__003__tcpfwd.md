
# vpn_gateway_spec__003__tcpfwd.md

## 1. Context

We now have the following in the VPN server:

- A **WinTun-based VPN path** on both client and server.
- A `Softellect.Vpn.Server.Nat` module that already handles **IPv4 + TCP + UDP** header rewriting and checksums at the packet level, via:
  - `Nat.translateOutbound (vpnSubnetUint, vpnMaskUint) externalIpUint packet`
  - `Nat.translateInbound externalIpUint packet`
- A `Softellect.Vpn.Server.ExternalInterface` module that currently implements **UDP-only** forwarding:
  - It extracts UDP payload + destination IP/port from NATted IPv4 packets.
  - It sends via a UDP socket (`SocketType.Dgram`, `ProtocolType.Udp`).
  - It receives UDP replies with `Socket.ReceiveFrom`.
  - It reconstructs IPv4/UDP headers and passes packets back to PacketRouter via the callback.

**Important:**
- The NAT module is already wired and must **not** be modified or reimplemented.
- The current `ExternalInterface` only handles UDP; **TCP is effectively not forwarded** at all.
- The user does **not** want to rely on OS-level NAT configuration; NAT must remain inside VpnServer.

The goal of this spec is to have Claude Code **actually implement TCP forwarding**, by refactoring the external interface to work at the **raw IPv4 packet** level for both TCP and UDP, using a raw socket, while continuing to rely on the existing NAT module for header rewriting.

The human (user) will handle **builds and runtime testing**. Claude Code must only generate code and comments, not simulate or assume runtime behavior.

---

## 2. High-level design

### 2.1 Current architecture (simplified)

- Client:
  - OS sends IP packets into client WinTun.
  - Client code reads raw IP packets from WinTun and sends them over WCF as `byte[]`.

- Server:
  - WCF service receives `byte[]` packets from client and passes them to `PacketRouter.injectPacket`, which injects them into server WinTun.
  - `PacketRouter.receiveLoop` reads packets from server WinTun and either:
    - Routes them back to VPN clients (when dest is inside VPN subnet), or
    - Uses the NAT module + ExternalInterface to send them towards the real internet (when dest is outside VPN subnet).

- NAT module:
  - Takes raw IPv4 packets, rewrites headers (IP + TCP/UDP), and returns a modified packet.

- ExternalInterface (current):
  - Only supports UDP by reconstructing IPv4/UDP on top of a UDP socket.

### 2.2 Target architecture

We want `ExternalInterface` to:

- Work on **full IPv4 packets** (both TCP and UDP), using a **raw IP socket**.
- Keep the NAT module as the **only place** where header rewriting is done.
- Make TCP and UDP both flow through the same “raw IPv4 gateway”.

Concretely:

- `PacketRouter` continues to:
  - Call `Nat.translateOutbound` for packets destined **outside** the VPN subnet.
  - Call `externalGateway.SendOutbound natPacket` with the **already NATted full IPv4 packet**.
- `ExternalInterface` is refactored to:
  - Use `Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)` (or equivalent) to send and receive **full IPv4 packets**.
  - For outbound packets, send the entire IPv4 packet buffer as-is (NAT has already adjusted IPs/ports/checksums).
  - For inbound packets from the raw socket, pass them through `Nat.translateInbound` and then into the provided `onPacketFromInternet` callback, which injects into WinTun via `PacketRouter`.

This avoids having to reconstruct headers per protocol and gives a unified path for both TCP and UDP.

**Note:** On Windows, raw sockets require admin privileges and may have OS limitations. This is acceptable; we just need Claude Code to generate the code; runtime behavior will be verified by the human.

---

## 3. Required changes: ExternalInterface

> File: `Vpn/Server/ExternalInterface.fs`  
> Namespace/module: `Softellect.Vpn.Server.ExternalInterface`

### 3.1 Public API must remain the same

Do **not** change the public API:

```fsharp
namespace Softellect.Vpn.Server

open System
open System.Net
open System.Net.Sockets

module ExternalInterface =

    type ExternalConfig =
        {
            serverPublicIp : IPAddress
        }

    type ExternalGateway =
        new : ExternalConfig -> ExternalGateway

        member Start : onPacketFromInternet:(byte[] -> unit) -> unit
        member SendOutbound : packet:byte[] -> unit
        member Stop : unit -> unit
```

No renames or signature changes. The implementation behind these members will be refactored.

### 3.2 Replace UDP-only design with raw IPv4 design

Claude Code must refactor `ExternalGateway` to:

1. Use **one raw IPv4 socket** to handle both TCP and UDP:

   ```fsharp
   let rawSocket =
       new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)
   ```

   - Set `SocketOptionName.HeaderIncluded` to `true` so we can send full IP packets:
     ```fsharp
     rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)
     ```
   - Bind to the server’s external IP if needed:
     ```fsharp
     let endPoint = new IPEndPoint(config.serverPublicIp, 0)
     rawSocket.Bind(endPoint)
     ```

2. `SendOutbound : packet:byte[] -> unit`:

   - `packet` is a **complete IPv4 packet** returned from `Nat.translateOutbound`.
   - Extract destination IP from the IPv4 header:
     - Version/IHL byte is at index 0; IPv4 dest IP starts at byte offset 16.
     - Build `IPEndPoint` with `dstIp` and port 0 (port is irrelevant at raw IP level for routing):
       ```fsharp
       let dstIp = IPAddress(BitConverter.ToUInt32(packet, 16) |> System.Net.IPAddress.HostToNetworkOrder |> int64)
       let remoteEndPoint = new IPEndPoint(dstIp, 0)
       rawSocket.SendTo(packet, remoteEndPoint) |> ignore
       ```
     - Claude Code can adjust endian conversions as needed; what matters is using the IP at offset 16–19.
   - Do **not** attempt to parse protocol or payload here. Just send the full packet.

3. `Start : onPacketFromInternet:(byte[] -> unit) -> unit`:

   - Start a background thread (`Thread` or `Task`) that:
     - Repeatedly calls `rawSocket.Receive` into a sufficiently large buffer (e.g. `byte[65535]`).
     - Trims the received data to the length returned by `Receive` into a new `byte[]` packet.
     - Calls the provided `onPacketFromInternet` callback with the received packet.

   - This loop must:
     - Catch exceptions and log errors using `Logger.logError`.
     - Honor an internal `running` flag checked each iteration to allow `Stop` to exit gracefully.

4. `Stop : unit -> unit`:

   - Set `running <- false`.
   - Join the receive thread (with a reasonable timeout).
   - Close/dispose the raw socket.
   - Log that the gateway has stopped.

5. Logging and robustness:

   - Log start/stop events with `Logger.logInfo`.
   - Log errors in the receive loop with `Logger.logError`.
   - Optionally log trace-level details for packets if needed (e.g., packet length, proto, src/dst IPs) behind `Logger.logTrace` calls.

### 3.3 Remove or bypass UDP-only reconstruction code

- Any existing code that:
  - Extracts UDP payload from a NATted packet,
  - Reconstructs IPv4/UDP headers on receive,
  - Relies on `SocketType.Dgram` and `ProtocolType.Udp`

  …should be **removed or bypassed** in favor of the raw IPv4 socket design above.

- There should be **no protocol-specific reconstruction** in ExternalInterface anymore. All protocol-specific header rewriting remains inside the existing `Nat` module.

- If there are helper functions that are now unused (e.g., UDP header builders), they may be deleted to keep the file clean.

---

## 4. Integration with PacketRouter and NAT

> File: `Vpn/Server/PacketRouter.fs`  
> Module: `Softellect.Vpn.Server.PacketRouter`

In a previous iteration, PacketRouter was already modified to:

- Instantiate `ExternalGateway` with the server’s public IP.
- On start, call something like:

  ```fsharp
  externalGateway.Start (fun rawPacketFromInternet ->
      match Nat.translateInbound externalIpUint rawPacketFromInternet with
      | Some translated ->
          // Inject into WinTun; PacketRouter.receiveLoop will handle routing to clients
          ignore (injectPacket translated)
      | None ->
          Logger.logTrace (fun () -> "NAT dropped inbound packet (no mapping)")
  )
  ```

- In the main receive loop, for packets whose destination is **outside the VPN subnet**:

  ```fsharp
  match Nat.translateOutbound (vpnSubnetUint, vpnMaskUint) externalIpUint packet with
  | Some natPacket ->
      externalGateway.SendOutbound natPacket
  | None ->
      Logger.logTrace (fun () -> "PacketRouter: outbound packet dropped by NAT.")
  ```

Claude Code must:

- **Keep this logic as-is** if it already exists.
- If the call to `externalGateway.SendOutbound` is missing, implement it per the above pattern.
- Do **not** modify the NAT module; assume it is correct and already integrated.

The net result after refactoring ExternalInterface to raw IP is:

- Outbound VPN packets destined to the internet:
  - WinTun → PacketRouter → NAT.translateOutbound → ExternalGateway.SendOutbound → raw IP socket → OS network stack.

- Inbound internet packets destined to the server’s external IP:
  - OS network stack → raw IP socket → ExternalGateway receive loop → NAT.translateInbound → PacketRouter.injectPacket → WinTun → client.

Both TCP and UDP are now handled uniformly at the raw IPv4 level.

---

## 5. Constraints and responsibilities

- Do **not** modify or reimplement `Softellect.Vpn.Server.Nat`.
- Do **not** change public APIs of `ExternalInterface.ExternalGateway` or `PacketRouter`.
- Do **not** touch client-side code in this iteration.
- It is acceptable to assume **administrative privileges** on Windows for raw socket usage.
- The human developer (not the model) will:
  - build the solution,
  - run the VPN,
  - verify runtime behavior (including whether raw sockets are allowed on the target OS).

Claude Code must generate:

1. A fully updated `ExternalInterface.fs` implementing the described raw IPv4 design.
2. Any minimal PacketRouter adjustments required to complete the flow (if not already present), but no large-scale refactors.
3. Clear comments where platform limitations may apply (e.g., raw sockets requiring admin).

---

## 6. Summary for Claude Code

- The goal is to **actually implement TCP forwarding** by moving ExternalInterface to a **raw IPv4 packet** gateway.
- NAT is already implemented and must remain the only component doing header rewriting for TCP/UDP.
- ExternalInterface must:
  - Send NATted packets out using a raw IPv4 socket.
  - Receive raw IPv4 packets from the internet and pass them to NAT + PacketRouter.
- The public surface (ExternalGateway / PacketRouter) remains unchanged.
- The user will compile and test; Claude Code only needs to generate code that matches this spec.
