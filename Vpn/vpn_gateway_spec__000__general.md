# vpn_gateway_spec__000__general

## Working directory
Claude Code must work only under:

`C:\GitHub\Softellect\Vpn\`

## Goal
Implement a user-space VPN gateway where **all traffic from VPN clients (10.66.77.0/24)** is forwarded through the VPN server and exits to the real internet using a user-space external interface + user-space NAT. No OS routing/NAT table changes are allowed.

The user (not Claude Code) will build and run tests after each change set.

## Current architecture (high level)

### Client side
- A WinTun adapter is created on the client.
- Client assigned IP is in the VPN subnet (example: `10.66.77.2/24`).
- The tunnel captures IPv4 packets from the WinTun adapter (`Tunnel.receiveLoop`) and enqueues them.
- Client code batches and sends captured packets to the server over a WCF `NetTcpBinding` service (`sendPacket`).
- Client receives batches of packets from the server (`receivePackets`) and injects them into WinTun (`Tunnel.injectPacket`).

### Server side
- A WinTun adapter is created on the server, assigned the VPN gateway IP (example: `10.66.77.1/24`).
- `PacketRouter.receiveLoop` receives packets from the server WinTun adapter and routes them:
  - If destination IP is inside the VPN subnet → enqueue packet to the correct VPN client session (by assigned IP).
  - If destination IP is outside the VPN subnet → forward to the internet:
    - Apply user-space NAT (`Nat.translateOutbound`) so src IP becomes server public IP and src port becomes an allocated NAT port.
    - Send the full IPv4 packet out to the internet using `ExternalGateway.sendOutbound` (raw IPv4 socket).

### Return traffic (internet → client)
- Server external interface (`ExternalGateway`) receives raw IPv4 packets from the internet.
- Received packets are passed to the NAT inbound path:
  - `Nat.translateInbound` finds the NAT mapping by the **destination port on the server public IP**.
  - It rewrites the destination IP/port back to the internal VPN client IP/port.
- The translated packet is injected into the server WinTun adapter.
- `PacketRouter.receiveLoop` receives that injected packet and routes it to the correct VPN client by destination IP.

### DNS
- A DNS proxy exists on the server (`Softellect.Vpn.Server.DnsProxy`) to forward DNS queries from VPN clients to upstream resolvers.
- DNS requests are IPv4 UDP packets. Correct NAT inbound/outbound handling for UDP is required for DNS to work reliably.

## Constraints / invariants
- Do not modify OS routing tables, RRAS, netsh NAT, Windows ICS, etc.
- Keep VPN subnet IPv4-only for now; IPv6 packets may be dropped.
- NAT and external gateway must operate on **raw IPv4 packets** (IP header included).
- Any change must be small and localized; avoid refactors unrelated to the bug being fixed.
