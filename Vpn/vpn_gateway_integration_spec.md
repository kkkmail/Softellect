
# vpn_gateway_integration_spec.md

## 1. Context

The VPN system consists of:

- A **client WinTun adapter** on Windows.
- A **server WinTun adapter** on Windows.
- A WCF channel carrying raw IPv4 packets (`byte[]`) between client and server.
- The module `Softellect.Vpn.Server.PacketRouter` which currently:
  - Receives packets from server WinTun,
  - Routes *intra-VPN* traffic,
  - Sends packets back to clients via WCF.

**What is missing:**  
Traffic **to/from the real internet** is *not* forwarded by VpnServer at all.  
There is no NAT, and no external interface forwarding layer.

**The `Softellect.Vpn.Server.Nat` module is already implemented and present in the codebase.  
DO NOT modify or rewrite that module.  
Only call its `translateOutbound` / `translateInbound` functions.**

---

## 2. Goal of this task

Claude Code must implement the **remaining missing pieces**:

1. **ExternalInterface** module – abstraction for sending/receiving packets from the real internet.  
2. **Integrate NAT + ExternalInterface inside PacketRouter** so that:
   - Packets from VPN → Server WinTun → NAT → external interface.
   - Packets from external interface → NAT → Server WinTun → clients.

NO OS ROUTE TABLE MODIFYING.  
NO OS-LEVEL NAT / RRAS / NETSH.  
Everything must remain **user-space**.

**The NAT module is already present. Claude Code must not re-implement it.**  
Claude must only **use** it.

---

## 3. Design Overview (what Claude Code must implement)

### 3.1 New module: `Softellect.Vpn.Server.ExternalInterface`

Claude must create a new file:

```
Vpn/Server/ExternalInterface.fs
```

It must define:

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

        /// Start background receive loop from external interface.
        /// onPacketFromInternet is a callback that PacketRouter will provide.
        member Start : onPacketFromInternet:(byte[] -> unit) -> unit

        /// Send NATted outbound packet to external network.
        member SendOutbound : packet:byte[] -> unit

        /// Stop everything.
        member Stop : unit -> unit
```

### Notes for Claude Code:

- **UDP-only support (v1)** is acceptable.
- TCP forwarding may be stubbed (not required for v1).
- For UDP packets:
  - Extract destination IP/port directly from the NATted packet’s IPv4 header.
  - Use `Socket.SendTo` to send the UDP payload.
  - Use `Socket.ReceiveFrom` in a background loop to capture UDP replies.
  - Claude should generate clean, readable F#.

Again: **I will build and test. Claude must not execute or guess runtime outputs.**

---

## 4. Integrate ExternalInterface + NAT inside PacketRouter

Claude must update `PacketRouter.fs` as follows:

### 4.1 Extend PacketRouterConfig

Add a new field:

```fsharp
serverPublicIp : Ip4
```

(This is the external IPv4 the NAT layer should rewrite source addresses to.)

### 4.2 Inside PacketRouter constructor

- Convert:
  - `serverPublicIp` to `uint32` (network byte order),
  - `vpnSubnet` and mask to `uint32`.

- Instantiate an `ExternalGateway`.

### 4.3 On PacketRouter.start()

When WinTun session successfully starts:

- Start the external gateway:

```fsharp
externalGateway.Start(onPacketFromInternet = fun rawPacket ->
    // Called when external gateway receives a packet from internet
    match Nat.translateInbound externalIpUint rawPacket with
    | Some translated ->
        // Feed it into WinTun (not to clients; the usual routing loop handles that)
        ignore (injectPacket translated)
    | None -> ()
)
```

### 4.4 Modify receiveLoop in PacketRouter

This is the most critical integration step.

Inside the main receive loop:

```fsharp
let packet = adp.ReceivePacket()
```

After successfully parsing destination IP:

1. If destination IP is **inside VPN subnet** → keep existing behavior:
   - Find client session by dest IP.
   - Enqueue packet for that client.

2. Otherwise (destination **outside** VPN subnet):
   - This is an outbound internet packet.
   - Call NAT outbound:

     ```fsharp
     match Nat.translateOutbound (vpnSubnetUint, vpnMaskUint) externalIpUint packet with
     | Some natPacket ->
         externalGateway.SendOutbound natPacket
     | None ->
         // drop, or log, but do not route back into VPN
     ```

### 4.5 No other modifications are needed

- Do **not** rewrite NAT logic.  
- Do **not** touch the client code yet.  
- Do **not** introduce OS-level routing; everything stays inside VpnServer.

---

## 5. Acceptance constraints (Claude Code must respect)

- Claude Code must **not** attempt to compile or run anything.
- Claude Code must **not** modify the existing NAT module.
- Claude must write clean, idiomatic F# for the new modules and PacketRouter modifications.
- UDP-only implementation of ExternalInterface is absolutely fine for v1.
- I (the human developer) will:
  - build the solution manually,
  - run tests,
  - debug runtime behavior.

Claude Code should assume:

- NAT module **exists and is correct**,
- PacketRouter.fs exists and is mostly unchanged except the additions described here,
- External interface will be implemented entirely by Claude Code.

---

## 6. Deliverables for Claude Code

Claude Code must output:

1. **A complete new file** `ExternalInterface.fs` implementing the UDP external gateway.
2. **A patch/diff** or updated `PacketRouter.fs` showing exactly:
   - how PacketRouterConfig changed,
   - how the gateway is instantiated,
   - how NAT outbound/inbound is integrated.
3. **No NAT module code** (since it already exists).
4. **No client-side modifications**.

---

## Summary

- The **NAT module already exists** — Claude Code must *use* it, not rewrite it.
- Claude Code must implement:
  - a new **UDP-only external interface**, and
  - modifications to **PacketRouter** to call NAT and external interface.
- I will compile and test everything afterwards.

This spec must be followed strictly.
