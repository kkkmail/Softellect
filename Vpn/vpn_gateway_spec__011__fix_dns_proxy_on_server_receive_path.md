# vpn_gateway_spec__011__fix_dns_proxy_on_server_receive_path.md

## Goal

Fix VPN DNS resolution by ensuring **client DNS queries to the VPN gateway IP (`serverVpnIp`, port 53/UDP)** are handled by the server-side DNS proxy **before** the packet is injected into the server WinTun adapter.

Current behavior: the server injects the client’s DNS packet into the Windows IP stack (`router.injectPacket(packet)`), so it **never reaches** `PacketRouter.receiveLoop` and therefore never triggers `DnsProxy.tryParseDnsQuery`.

Target behavior: when the server receives a client packet that is a DNS query to `serverVpnIp:53/UDP`, the server must:
1) forward the DNS query to upstream DNS (`1.1.1.1:53`) using `DnsProxy.forwardDnsQuery`,
2) enqueue the resulting reply packet back to the same client using `ClientRegistry.enqueuePacketForClient`,
3) return `Ok ()` from `sendPacket` **without** injecting the DNS query into WinTun.

## Working directory

All work must be done under:

`C:\GitHub\Softellect\Vpn\`

## Files to change

- `C:\GitHub\Softellect\Vpn\Server\Service.fs`

## Files to NOT change

- Do not change any files under `.git\`
- Do not change client code
- Do not change NAT or ExternalInterface
- Do not change `Server\PacketRouter.fs` in this change (leave it as-is even if DNS parsing there becomes redundant)

## Implementation steps

### 1) Add `serverVpnIpUint` to `VpnService` (computed once)

In `C:\GitHub\Softellect\Vpn\Server\Service.fs`, inside `type VpnService(data: VpnServerData) =` add a local helper and compute `serverVpnIpUint`.

Use the same conversion logic as `PacketRouter.ipToUInt32`:

- Split `IpAddress.value` into 4 bytes
- Combine into `uint32` in network byte order:
  `(uint32 b0 <<< 24) ||| (uint32 b1 <<< 16) ||| (uint32 b2 <<< 8) ||| uint32 b3`

Compute it from `routerConfig.serverVpnIp.value` (type `IpAddress`).

Name the value:

- `let serverVpnIpUint = ...`

### 2) Intercept DNS in `IVpnService.sendPacket`

In `Service.fs`, locate:

```fs
member _.sendPacket (clientId, packet) =
    match registry.tryGetSession(clientId) with
    | Some _ ->
        registry.updateActivity(clientId)
        Logger.logTrace (fun () -> $"Server received packet from client {clientId.value}, size={packet.Length} bytes, packet=%A{(summarizePacket packet)}")

        match router.injectPacket(packet) with
        | Ok () -> Ok ()
        | Error msg -> Error (ConfigErr msg)
    | None ->
        Error (clientId |> SessionExpiredErr |> ServerErr)
```

Replace the body of the `| Some _ ->` branch so that it:

1) Updates activity (keep as-is).
2) Logs the “Server received packet...” line (keep as-is).
3) Calls `DnsProxy.tryParseDnsQuery serverVpnIpUint packet`.

If `tryParseDnsQuery` returns `Some (srcIp, srcPort, dnsPayload)`:

- Call `DnsProxy.forwardDnsQuery serverVpnIpUint srcIp srcPort dnsPayload`.

If that returns `Some replyPacket`:

- Call `registry.enqueuePacketForClient(clientId, replyPacket) |> ignore`
- Add a trace log:
  - Prefix: `DNSPROXY:`
  - Include clientId, reply length.

If `forwardDnsQuery` returns `None` (timeout/error already logged in `DnsProxy`):

- Do not inject into TUN.
- Return `Ok ()` (so the client loop continues).

In all DNS cases (Some/None reply), `sendPacket` must return `Ok ()` and **must not** call `router.injectPacket(packet)`.

If `tryParseDnsQuery` returns `None`, fall back to the current behavior:

- `router.injectPacket(packet)` and return `Ok ()` / `Error (ConfigErr msg)` as before.

### 3) Safety/consistency checks

- You may optionally verify that `srcIp` equals the session’s `assignedIp` (if you have the session record available), but do **not** reject packets based on this check in this change. Logging-only if you implement it.
- Do not allocate long-lived `UdpClient` instances; keep existing `DnsProxy.forwardDnsQuery` usage as-is.

## Expected logs after the fix

### Server log (`-a__...`)

When the client sends DNS to `10.66.77.1:53`, you must now see trace lines like:

- `DNSPROXY OUT: ... qlen=... upstream=1.1.1.1:53`
- `DNSPROXY IN: upstream response len=...`
- `DNSPROXY: enqueued reply to client ... len=...`

and you should **not** see the DNS query being routed as “No client found for destination IP: 10.66.77.1” (because it never goes into TUN for routing).

### Client log (`-c__...`)

You should see the client receiving DNS reply packets and then normal name resolution-dependent traffic proceeding.

## Build and run checklist

1) Build Server and Client.
2) Start server service.
3) Start client and connect.
4) Trigger a DNS lookup (e.g., open a webpage or run `nslookup example.com`).
5) Confirm:
   - Server shows `DNSPROXY OUT/IN` lines and `enqueued reply`.
   - Client shows receipt of packets corresponding to DNS replies.
   - Name resolution succeeds.

## Definition of done

- DNS queries to `serverVpnIp:53/UDP` from the client result in upstream DNS lookups and replies being delivered back to the originating client.
- No changes outside `Server\Service.fs`.
