
# vpn_gateway_tcp_followup_spec.md

## 1. Context

The current VPN server now has:

- A **WinTun-based VPN path** on both client and server.
- A `Softellect.Vpn.Server.Nat` module that already handles **IPv4 + TCP + UDP** header rewriting and checksums at the packet level.
- A `Softellect.Vpn.Server.ExternalInterface` module that is **UDP-only**:
  - It extracts UDP payloads + destination IP/port from NATted IPv4 packets.
  - It sends them via a UDP socket with `Socket.SendTo`.
  - It receives UDP replies with `Socket.ReceiveFrom`.
  - It reconstructs IPv4+UDP packets and passes them back to `PacketRouter` via the callback.

**Important constraints:**

- We MUST NOT modify or rewrite the `Nat` module.
- We MUST NOT touch OS routing tables or rely on OS-level NAT/RRAS.
- We MUST NOT attempt to implement a full custom TCP stack in this iteration.
- The human developer (not the model) will build and run all tests; do not simulate or assume runtime output.

The user suspects (correctly) that **TCP traffic is not actually being forwarded** to/from the internet, even though the NAT module is theoretically capable of rewriting TCP headers. The missing piece is that `ExternalInterface` does not handle TCP at all — only UDP.

This document describes what **Claude Code** should do in the *next* iteration.

---

## 2. Scope of this iteration

This iteration is intentionally conservative:

1. **Keep ExternalInterface UDP support working as-is.**
2. **Make TCP handling explicit, non-magic, and safe:**
   - Detect TCP packets in `ExternalInterface.SendOutbound`.
   - For now, **log and drop** them in a controlled way.
3. **Prepare the code structure and comments so that a later iteration can implement a proper TCP solution (likely via a proxy-style approach or a driver like WinDivert).**

In other words: we are not implementing full TCP gateway behavior here; we are making UDP support explicit and ensuring TCP is **not silently “half-supported”**.

---

## 3. ExternalInterface: required changes

> File: `Vpn/Server/ExternalInterface.fs`  
> Namespace/module: `Softellect.Vpn.Server.ExternalInterface`

### 3.1 Do NOT change public API

The following public API should remain exactly as it is:

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

Do not rename these types or members.

### 3.2 Add protocol detection inside SendOutbound

Inside the implementation of `ExternalGateway.SendOutbound`, Claude Code must:

1. Inspect the **IPv4 header** in the `packet: byte[]`:

   - Protocol byte is at offset 9 in the IPv4 header:
     ```fsharp
     let protocol = packet[9]
     ```
   - For now, we only care about:
     - `6uy` → TCP
     - `17uy` → UDP

2. Branch behavior:

   ```fsharp
   match protocol with
   | 17uy ->
       // existing UDP forwarding logic (send via udpSocket / SendTo)
   | 6uy ->
       // TCP (unimplemented)
       Logger.logTrace (fun () ->
           "ExternalGateway.SendOutbound: TCP packet encountered; TCP forwarding is not implemented in v1. Packet will be dropped.")
       // Do NOT throw; just return unit.
   | other ->
       Logger.logTrace (fun () ->
           $"ExternalGateway.SendOutbound: unsupported protocol={other}, dropping packet.")
   ```

3. The **existing UDP forwarding logic** must stay intact. Claude Code should simply wrap existing code in the `| 17uy ->` branch.

4. There is **no need** to change the receive loop for UDP; it can keep reconstructing IPv4+UDP packets and invoking `onPacketFromInternet`.

### 3.3 Add high-level TODOs for TCP

Claude Code must add a concise comment block above `SendOutbound` and/or above the internal UDP socket logic:

```fsharp
/// NOTE (v1):
/// - This gateway currently supports **UDP-only** external forwarding.
/// - TCP packets are detected and explicitly dropped with a trace log.
/// - A future iteration will implement TCP support via a proxy-style approach
///   or a driver-based packet capture (e.g. WinDivert) on the external side.
/// - Do NOT attempt to implement a custom TCP stack here.
```

Do not implement TCP flows in this iteration; just mark the limitation clearly.

---

## 4. PacketRouter: minimal confirmation (no major changes)

> File: `Vpn/Server/PacketRouter.fs`  
> Namespace/module: `Softellect.Vpn.Server.PacketRouter`

Claude Code should **only confirm** that:

- Packets with **destination outside the VPN subnet** are already handled by something like:

  ```fsharp
  match Nat.translateOutbound (vpnSubnetUint, vpnMaskUint) externalIpUint packet with
  | Some natPacket ->
      externalGateway.SendOutbound natPacket
  | None ->
      Logger.logTrace (fun () -> "PacketRouter: outbound packet dropped by NAT.")
  ```

- Packets with **destination inside VPN subnet** are routed back to VPN clients as before.

Claude Code does **not** need to modify this logic in this iteration.

If the code is already wired this way, leave it as-is. If the call to `externalGateway.SendOutbound` is missing, it should be implemented per the previous spec you already followed.

---

## 5. What Claude Code must NOT do

- Do **not** modify the `Nat` module.
- Do **not** try to invent a full TCP stack or raw TCP NAT engine.
- Do **not** change the public signatures of `ExternalGateway` or `PacketRouter`.
- Do **not** touch client-side code (`Tunnel.fs`, client `Service.fs`, etc.).
- Do **not** introduce new external dependencies (libraries) without an explicit follow-up spec.

---

## 6. Responsibility for testing

- Claude Code must treat this spec as a **code-generation request only**.
- Claude must not assume that the code compiles or runs; it must not invent runtime results.
- The **human developer** (the user) will:
  - build the solution,
  - run the VPN,
  - verify that:
    - UDP traffic (e.g., DNS) is correctly forwarded via the VPN,
    - TCP packets are logged and dropped in a controlled manner.

---

## 7. Summary for Claude Code

1. Keep `ExternalInterface` UDP behavior untouched but **wrap it in explicit protocol branches**.
2. Add explicit handling for TCP packets: **log and drop** in `SendOutbound`.
3. Add clear comments stating that:
   - TCP is *not implemented*,
   - a future iteration will tackle TCP with a proper design.
4. Leave `PacketRouter` logic intact, as long as it already:
   - routes VPN-internal traffic by dest IP,
   - sends non-VPN-destined packets to `Nat.translateOutbound` + `ExternalGateway.SendOutbound`.

This gives the human developer a clean, explicit baseline where:

- UDP is supported end-to-end,
- TCP is not silently “sort of” supported,
- The next TCP-focused design can be done deliberately in a separate spec.

