
# vpn_gateway_spec__005__ipv4only.md

## 1. Context

We now have:

- A working WinTun-based VPN path (client + server).
- A user-space NAT module (`Nat.fs`) that is **IPv4-only** (by design).
- A raw-IP `ExternalInterface` that forwards full IPv4 packets to/from the internet.
- Logging that shows both IPv4 and IPv6 packets being captured at the client and server.

DNS over VPN still times out.

From the logs, we see that **most (or all) DNS-related traffic is IPv6**, while:

- The NAT code only understands IPv4 headers.
- The gateway is designed for IPv4 packet routing.
- There is no IPv6 NAT or IPv6 gateway logic.

Therefore, IPv6 packets entering the tunnel **cannot be forwarded correctly** and effectively get “black-holed” in the VPN path, causing DNS timeouts (and other IPv6-based traffic failures).

The goal of this iteration is to **explicitly support IPv4 only in the VPN data path**, and cleanly **drop IPv6 packets** at the tunnel boundaries with minimal logging, instead of trying to partially handle them.

The human developer (the user) will build and run the tests; Claude Code must only generate code changes.

---

## 2. Scope of this iteration

Claude Code must:

1. Update the **client-side tunnel** code to **only send IPv4 packets** through the VPN.
2. Update the **server-side packet routing** code to **only process IPv4 packets** from the server WinTun adapter.
3. Add **small, throttled trace logs** to make IPv6 drops visible without flooding the logs.

Do **NOT**:

- Implement IPv6 NAT or IPv6 forwarding.
- Change the public APIs of existing types/modules.
- Modify the NAT module (`Nat.fs`) or `ExternalInterface.fs` in this iteration.

Focus is on **client `Tunnel.fs`** and **server `PacketRouter.fs`**.

---

## 3. Client-side changes: Tunnel.fs

> File: `Vpn/Client/Tunnel.fs` (exact path/name may differ slightly, but it is the module that owns the client-side WinTun receive/send loops).

### 3.1 Add a helper to determine IP version

At the top of the module (or near other helpers), add:

```fsharp
let private getIpVersion (packet: byte[]) =
    if packet.Length = 0 then 0
    else int packet[0] >>> 4
```

### 3.2 In the receive loop from TUN → server

Find the loop where packets are read from the client WinTun adapter and enqueued or sent to the server (via WCF `sendPacket`). Wrap the logic with an IPv4 check.

Pseudo-code (Claude must adapt to actual code structure):

```fsharp
let rec receiveLoop () =
    while running do
        try
            let packet = adapter.ReceivePacket()
            if not (isNull packet) && packet.Length > 0 then
                let v = getIpVersion packet
                match v with
                | 4 ->
                    // Existing behavior: send/queue this packet to the VPN server
                    // (DO NOT change the actual routing logic, just keep it in the IPv4 branch)
                    enqueueOrSendToServer packet
                | 6 ->
                    // Drop IPv6 packets (do not send to server)
                    // Optional throttled log:
                    logDroppedIpv6OnceInAWhile packet
                | _ ->
                    // Unknown or malformed; drop silently or log once in a while
                    ()
            else
                // No packet available; small sleep if existing code does that
                ()
        with ex ->
            // Existing exception handling
            ()
```

Claude must:

- Place the existing “send to server” logic **inside** the `| 4 ->` branch.
- For `| 6 ->`, introduce a *throttled* trace log to show IPv6 being dropped without spamming logs.

Example throttled logging helper:

```fsharp
let mutable lastIpv6Log = System.DateTime.MinValue

let private logDroppedIpv6OnceInAWhile (packet: byte[]) =
    let now = System.DateTime.UtcNow
    if (now - lastIpv6Log).TotalSeconds > 5.0 then
        lastIpv6Log <- now
        Logger.logTrace (fun () -> $"Tunnel: dropping IPv6 packet, len={packet.Length}")
```

Use this helper in the `| 6 ->` case.

### 3.3 DO NOT change client send/receive API

- Do not change how WCF calls are made.
- Only filter which packets are sent upstream to the server.

---

## 4. Server-side changes: PacketRouter.fs

> File: `Vpn/Server/PacketRouter.fs`

The server’s `PacketRouter` has a `receiveLoop` that reads packets from the server WinTun adapter and either:

- Routes them to another VPN client (intra-VPN), or
- Sends them to NAT + ExternalInterface for internet access.

We must ensure **only IPv4 packets** are processed in this loop.

### 4.1 Add a helper to determine IP version

Near other helpers in `PacketRouter`, add:

```fsharp
let private getIpVersion (packet: byte[]) =
    if packet.Length = 0 then 0
    else int packet[0] >>> 4
```

### 4.2 Wrap receiveLoop processing with IPv4 check

Inside `receiveLoop`, where we currently do something like:

```fsharp
let packet = adp.ReceivePacket()
if not (isNull packet) then
    // existing logic: getDestinationIp, find client, or NAT outbound
```

Change it to:

```fsharp
let packet = adp.ReceivePacket()
if not (isNull packet) && packet.Length > 0 then
    let v = getIpVersion packet
    match v with
    | 4 ->
        // Existing IPv4 logic:
        // - parse destination IP
        // - if inside VPN subnet: route to client
        // - else: NAT.translateOutbound + externalGateway.SendOutbound
        handleIpv4Packet packet
    | 6 ->
        // Drop IPv6 packets coming from server WinTun
        logDroppedIpv6OnceInAWhile packet
    | _ ->
        // Unknown/malformed, ignore or low-frequency log
        ()
else
    // no packet; existing sleep/backoff if any
    ()
```

Claude must factor the existing handling into a local function `handleIpv4Packet` or inline in the `| 4 ->` branch, but **do not change the IPv4 behavior itself**.

Example throttled IPv6 drop logging (similar to client):

```fsharp
let mutable lastIpv6Log = System.DateTime.MinValue

let private logDroppedIpv6OnceInAWhile (packet: byte[]) =
    let now = System.DateTime.UtcNow
    if (now - lastIpv6Log).TotalSeconds > 5.0 then
        lastIpv6Log <- now
        Logger.logTrace (fun () -> $"PacketRouter: dropping IPv6 packet from WinTun, len={packet.Length}")
```

Use this in the `| 6 ->` case.

### 4.3 Do not touch NAT or ExternalInterface

- NAT operates at IPv4 level and is already invoked only for packets interpreted as IPv4.
- ExternalInterface forwards full IPv4 packets — IPv6 is out of scope.

---

## 5. No changes to NAT.fs or ExternalInterface.fs

This iteration does **not** require any edits to:

- `Nat.fs`  
- `ExternalInterface.fs`  
- Any service / WCF code.

Those modules already assume IPv4-only semantics, which we are now making explicit at the tunnel boundaries.

---

## 6. Expected result after these changes

After Claude Code implements the above:

1. The client will **no longer send IPv6 packets through the VPN**.
2. Windows networking will be forced to use IPv4 over the VPN interface for DNS and other traffic.
3. On DNS tests like:

   ```powershell
   nslookup google.com 8.8.8.8
   ```

   you should now see IPv4 UDP DNS packets in your logs and, assuming NAT + ExternalInterface are correct, **get actual DNS responses** on the client.

If DNS still fails after this change, further debugging will be focused **only on IPv4** (NAT mapping, raw socket behavior, firewall), instead of being confused by IPv6 that cannot be NATted.

---

## 7. Deliverables for Claude Code

Claude Code must output:

1. A patch to **client Tunnel.fs** that:
   - Adds an IP version helper,
   - Filters out non-IPv4 packets before sending to server,
   - Adds throttled IPv6-drop logging.

2. A patch to **server PacketRouter.fs** that:
   - Adds an IP version helper,
   - Filters out non-IPv4 packets from WinTun,
   - Adds throttled IPv6-drop logging.

3. No changes to:
   - `Nat.fs`,
   - `ExternalInterface.fs`,
   - Service / WCF code,
   - Public APIs.

The human developer will compile and run to test DNS over the VPN after these changes.
