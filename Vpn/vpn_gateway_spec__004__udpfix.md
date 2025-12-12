
# vpn_gateway_spec__004__udpfix.md

## Purpose

DNS over VPN is failing even though NAT outbound works correctly.  
Server logs show **only proto=6 (TCP)** arriving via the raw socket, and **no proto=17 (UDP)**, meaning:

- UDP replies from DNS servers are not being captured at all, or
- The raw socket is not in "receive all" mode, or
- The receive loop ignores UDP packets.

This iteration tells Claude Code to **fix UDP inbound handling** without adding noisy logging.

---

## Scope

Claude Code must update **ExternalInterface.fs** ONLY.

Do **NOT** modify:

- Nat.fs  
- PacketRouter.fs  
- Service.fs  
- Public API signatures  

We ONLY fix raw-socket initialization + UDP handling.

---

## Required Fixes

### 1. Ensure raw socket receives ALL IPv4 packets (TCP + UDP)

Claude must modify raw socket initialization to:

```fsharp
let rawSocket =
    new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)

rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true)
rawSocket.Bind(IPEndPoint(config.serverPublicIp, 0))

// REQUIRED for Windows to deliver ALL IPv4 packets (including UDP)
let inVal  = BitConverter.GetBytes(1)        // enable = 1
let outVal = Array.zeroCreate<byte> 4
rawSocket.IOControl(IOControlCode.ReceiveAll, inVal, outVal)
```

This single line is the difference between **only TCP** and **TCP+UDP** inbound.

---

### 2. Update receive loop to *process* UDP packets

Inside the receive loop where trimmedPacket is created, Claude must:

```fsharp
let protocol = trimmedPacket.[9]     // IP protocol byte

match protocol with
| 17uy ->    // UDP
    Logger.logTrace(fun () -> "ExternalGateway: UDP packet received (throttled)")
    onPacketFromInternet trimmedPacket

| 6uy ->     // TCP
    Logger.logTrace(fun () -> "ExternalGateway: TCP packet received (throttled)")
    onPacketFromInternet trimmedPacket

| _ ->
    // silently drop or add very low verbosity trace
    ()
```

**Important:**  
Claude must *not* parse payloads or rebuild headers — NAT expects the full IPv4 packet.

---

### 3. Minimal throttled logging (no spam)

Claude must implement low-rate logging such as:

```fsharp
if protocol = 17uy then
    let now = DateTime.UtcNow
    if (now - lastUdpLog).TotalSeconds > 1.0 then
        lastUdpLog <- now
        Logger.logTrace(fun () -> "ExternalGateway: inbound UDP (1/sec)")
```

DO NOT dump buffers or print per-packet logs.

---

### 4. Ensure SendOutbound sends full IPv4 packet for UDP

Claude must verify:

- `SendOutbound` uses the raw socket (`rawSocket.SendTo(packet, remoteEP)`),
- No leftover code reconstructing UDP headers,
- Destination IP extracted from bytes 16–19 of the packet.

---

## Expected After This Fix

### On DNS query:

```
nslookup google.com 8.8.8.8
```

Server log should show:

1. NAT OUT (already works today)
2. ExternalGateway: inbound UDP (throttled)
3. NAT IN mapping externalPort → clientPort
4. PacketRouter injects inbound DNS reply into WinTun
5. Client receives DNS response

---

## Deliverables for Claude Code

Claude Code must output:

1. A patch for **ExternalInterface.fs** implementing:
   - IOControl(ReceiveAll)
   - UDP handling in receive loop
   - Minimal throttled logging
   - Direct forwarding of full IPv4 packets

2. No changes to any other files.

3. No excessive logging.

---

## Summary

This spec fixes the **critical issue preventing DNS from working**:

> Raw socket must be placed in ReceiveAll mode AND UDP packets must be forwarded into NAT inbound.

This unlocks DNS and all UDP-based traffic through the VPN tunnel.

