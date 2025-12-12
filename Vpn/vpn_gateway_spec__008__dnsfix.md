# vpn_gateway_spec__008__dnsfix.md

## Goal

DNS queries from the VPN client to the VPN “gateway” IP (`10.66.77.1:53`) must receive a reply.

Right now the client clearly generates UDP DNS packets to `10.66.77.1:53` (e.g. `10.66.77.2:60635 -> 10.66.77.1:53`) and sends them to the server over WCF, and the server injects them into the server’s WinTun adapter — but no response ever comes back to the client. That strongly suggests: **no DNS handler is actually producing a response packet back into the VPN path**.

You (Claude Code) will implement a *minimal, in-process DNS forwarder* in `VpnServer` (no OS routing/NAT changes, no Windows DNS service dependency), so that `10.66.77.1:53` works as a DNS server for VPN clients.

**Important:** I (the user) will build and test. You must keep changes small, mechanical, and easy to audit.

---

## What the logs say (do not ignore)

- Client generates and tunnels a DNS UDP packet destined to `10.66.77.1:53`.
- Server receives that packet via WCF and injects it into the WinTun adapter.
- There is no subsequent server-side indication of:
  - forwarding that DNS query to an upstream resolver, or
  - constructing a DNS reply packet and enqueueing it back to the client.

So: implement DNS forwarding explicitly in server code.

---

## Design constraints

1. **No OS routing / NAT / DNS service configuration**.
2. **Works for IPv4-only** (ignore IPv6 DNS for now).
3. Minimal implementation is OK (single client OK, but must not be obviously wrong for multiple clients).
4. Avoid “packet flood logging”. Add only a few strategically placed INFO/TRACE logs.

---

## Implementation approach

### A) Intercept DNS requests in `PacketRouter.receiveLoop`

When `PacketRouter` receives a packet from the WinTun adapter:
- Parse IPv4 header.
- If protocol is UDP (17),
- and destination IP is `config.serverVpnIp` (expected `10.66.77.1`),
- and destination port is `53`,
then **do NOT route to a client, and do NOT NAT**.

Instead, treat it as a DNS request from a VPN client and forward it upstream.

### B) Add a new module `DnsProxy.fs` under `namespace Softellect.Vpn.Server`

The module should expose something like:

- `type DnsProxyConfig = { upstreamDnsIp : Ip4; upstreamDnsPort : int; timeoutMs : int }`
- `type DnsProxy(onReplyToClient: VpnClientId * byte[] -> unit, cfg: DnsProxyConfig)`
  - `member HandleDnsRequest(clientId: VpnClientId, clientSrcIp: Ip4, clientSrcPort: int, requestPayload: byte[]) : unit`

Minimal behaviour:
1. Send `requestPayload` via UDP to `cfg.upstreamDnsIp:cfg.upstreamDnsPort`.
2. Wait for a UDP reply (timeout).
3. If a reply arrives:
   - Construct an IPv4+UDP packet that looks like it came from `10.66.77.1:53` to `clientSrcIp:clientSrcPort`
   - payload is the DNS reply bytes
   - compute IPv4 header checksum and UDP checksum (reuse existing helpers if you already have checksum code).
4. Call `onReplyToClient(clientId, replyPacket)` which will enqueue the packet back to that client.

### C) Enqueue DNS replies back to the correct VPN client

Do **not** inject the DNS reply into the server’s WinTun adapter (that just feeds the server OS again).

Instead, enqueue it directly into the correct client’s outgoing queue via the existing registry mechanism:

- `registry.enqueuePacketForClient(clientId, replyPacket)`

---

## Required edits

### 1) Add `DnsProxy.fs` (new file)

- Namespace: `Softellect.Vpn.Server`
- Keep it self-contained.
- Use `UdpClient` or `Socket` for upstream.
- Add minimal logging:
  - TRACE: “dns request forwarded” (clientId + txid if easy),
  - TRACE: “dns reply received” (length),
  - WARN: timeout.

### 2) Modify `PacketRouter.fs`

- Instantiate `DnsProxy` when starting the packet router (or lazily on first packet).
- In `receiveLoop`, add early handling:

Pseudo:
```fsharp
match tryParseIpv4Udp packet with
| Some(srcIp, dstIp, srcPort, dstPort, udpPayload) when dstIp = serverVpnIp && dstPort = 53 ->
    match findClientByIp srcIp with
    | Some session -> dnsProxy.HandleDnsRequest(session.clientId, srcIp, srcPort, udpPayload)
    | None -> logTrace "dns request from unknown src ip"
| _ ->
    // existing logic: route-to-client if dst in vpn subnet else NAT outbound
```

### 3) Build order

Update `Server.fsproj` compile order so `DnsProxy.fs` is compiled before `PacketRouter.fs`.

---

## Packet building details (must be correct)

To build reply packet:

- IPv4 header:
  - Version=4, IHL=5
  - Total length = 20 + 8 + payload.Length
  - TTL=64 (or 128, but be consistent)
  - Protocol=17
  - Src IP = `config.serverVpnIp` (10.66.77.1)
  - Dst IP = clientSrcIp (10.66.77.2)
  - Header checksum must be correct

- UDP header:
  - Src port = 53
  - Dst port = clientSrcPort
  - Length = 8 + payload.Length
  - UDP checksum should be computed (IPv4 allows 0, but do it properly).
  - Use pseudo-header checksum.

If checksum helpers already exist in `ExternalInterface.fs` or `Nat.fs`, reuse them (do not duplicate).

---

## Acceptance test (what I will run)

On client machine:
1. Connect VPN.
2. Run:
   - `nslookup example.com 10.66.77.1`
   - and/or `Resolve-DnsName example.com -Server 10.66.77.1`
3. Expect a reply within 1–2 seconds.

---

## If you cannot implement without seeing code

If you discover that:
- there is no existing IPv4/UDP parsing helper you can safely reuse, or
- checksum helpers are missing / wrong,

then stop and ask me for **exact file(s)** you need (by path + why), instead of guessing.
