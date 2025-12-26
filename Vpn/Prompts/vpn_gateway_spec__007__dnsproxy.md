# vpn_gateway_spec__007__dnsproxy.md

## Goal

DNS requests from the VPN client are sent to the VPN gateway IP **10.66.77.1:53** (server-side tun IP), and currently they time out because the server does **not** treat this as “internet-bound” traffic.

Evidence from the client log:
- The tunnel captures DNS queries addressed to the gateway: `IPv4: 10.66.77.2:<ephemeral> -> 10.66.77.1:53, proto=17` fileciteturn15file13L15-L16 and fileciteturn15file11L8-L9

So the server must **act as a DNS proxy/server** on its VPN IP, forwarding queries to an upstream resolver and injecting replies back into the VPN.

**Konstantin will build and test**. Claude Code should only implement the changes described here.

---

## What to implement

### A. Add a minimal DNS proxy on the server (UDP only)

Implement a new module/file in the server project:

**`Softellect.Vpn.Server.DnsProxy`** (new file `DnsProxy.fs` under the server project)

Responsibilities:
1. Parse incoming IPv4 packets and detect **UDP dstPort=53** where **dstIp == config.serverVpnIp** (the VPN gateway IP).
2. Extract the UDP payload (DNS query bytes).
3. Forward that payload via a normal UDP socket to an upstream DNS server (hardcode initially, e.g. `1.1.1.1:53`, optionally allow config later).
4. Receive the response and reconstruct an IPv4+UDP packet:
   - srcIp = `config.serverVpnIp` (10.66.77.1)
   - srcPort = 53
   - dstIp = client srcIp (e.g. 10.66.77.2)
   - dstPort = client srcPort (the ephemeral port from the original query)
   - payload = DNS response bytes
   - recompute IPv4 header checksum and UDP checksum (or UDP checksum = 0 for IPv4 is allowed, but prefer computing correctly if you already have helpers).
5. Inject the reconstructed packet into WinTun via `PacketRouter.injectPacket`.

Mapping/state:
- For MVP, you can do “inline” request/response in the PacketRouter receive loop (synchronous) **only for DNS**.
- If you want to do it properly (recommended): create a small concurrent map keyed by `(clientIp, clientSrcPort, txid)` to match responses, but **for UDP DNS** a simple synchronous `Send` then `Receive` with a short timeout (e.g. 2s) is acceptable for the first spin.

Timeouts:
- Set a receive timeout on the UDP socket (e.g. 2000ms) and log timeout explicitly.

Logging:
- Keep logs short and rate-limited by nature (DNS is low volume). Do **not** add raw packet hex dumps in the default path.
- Add trace logs like:
  - `DNSPROXY OUT: 10.66.77.2:64972 -> 10.66.77.1:53 qlen=... upstream=1.1.1.1:53`
  - `DNSPROXY IN: upstream response len=... -> 10.66.77.2:64972`
  - `DNSPROXY TIMEOUT: ...`

---

### B. Wire DNS proxy into `PacketRouter.receiveLoop`

In `PacketRouter.fs`, within the TUN receive loop logic:
1. When you parse `destIp` and `srcIp`, add a new branch **before** “route to VPN client”:
   - If `destIp == config.serverVpnIp.value` and protocol is UDP and dstPort == 53:
     - Call `DnsProxy.forwardDnsUdp` (or similar) to obtain a reply packet (byte[] option).
     - If reply packet exists, inject it into the adapter (via `adp.SendPacket` or existing `injectPacket` helper).
     - Do **not** enqueue this packet to any client session.
     - Continue loop.

2. Preserve the existing routing behavior for:
   - packets whose dest is within VPN subnet and matches a client
   - packets outside VPN subnet to go via NAT/external gateway

This avoids the current “dead zone” where packets to 10.66.77.1 are neither NAT’ed nor delivered to a client.

---

## Implementation details (don’t skip)

### IPv4/UDP parsing
You need:
- Validate min lengths: IPv4 header >= 20 bytes; verify IHL.
- Ensure packet is IPv4 (version 4).
- Protocol byte (offset 9) == 17 for UDP.
- srcIp bytes 12..15, dstIp bytes 16..19
- UDP header begins at `ipHeaderLen`
- UDP dstPort = bytes at `udpOffset+2..+3` (big endian)
- UDP srcPort = bytes at `udpOffset+0..+1`

### Packet building
You can reuse/duplicate helpers you already have in `ExternalInterface.fs` if present:
- Build IPv4 header (20 bytes, no options)
- Build UDP header (8 bytes)
- Compute checksums

If checksum helpers exist already, use them. If not:
- IPv4 checksum: standard 16-bit ones-complement over header words.
- UDP checksum: pseudo-header + udp header + payload (ones-complement). If too heavy for MVP, set UDP checksum = 0 (valid for IPv4 UDP), but log that it’s intentionally omitted.

---

## Files to change / add

Add:
- `Server/DnsProxy.fs` (new)

Modify:
- `Server/PacketRouter.fs`
  - integrate DNS proxy branch described above
- `Server.fsproj`
  - include `DnsProxy.fs` before `PacketRouter.fs`

Do **not** change client code for this spec.

---

## How Konstantin will test (so implement accordingly)

1. Run client+server, ensure the client still produces DNS queries to `10.66.77.1:53` (as in logs) fileciteturn15file13L15-L16.
2. On server logs, expect to see DNSPROXY OUT/IN lines.
3. On client, `nslookup openai.com` (or similar) should return quickly.
4. If DNS works, next step is general UDP/TCP forwarding coverage, but **this spec is DNS-only**.

---

## Notes / gotchas

- Do not use OS-level NAT or routing table changes.
- Don’t bind to port 53 on the server OS. This is packet-level proxying: you are replying via TUN injection, not listening on a normal UDP 53 socket.
- Upstream resolver should be reachable from the server’s public interface.
