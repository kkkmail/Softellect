
# vpn_gateway_spec__006__natinfix.md

## Purpose

DNS over VPN is still failing and the server logs are noisy and misleading because `Nat.translateInbound` is currently **too permissive**.

Right now, `translateInbound` effectively does:

> If the packet is NOT addressed to `externalIp`, return `Some packet`

This is wrong for a VPN NAT gateway because it causes unrelated external traffic (including the server’s own outbound traffic and random internet traffic) to be treated as “VPN inbound” and injected into the VPN TUN adapter.

This iteration fixes **only** the inbound NAT selection rule:

> Only packets whose destination IP is exactly `externalIp` are eligible for NAT inbound translation. Everything else must be dropped (`None`).

No throttled logging in this iteration (per request). Keep logs simple and focused.

---

## Scope

Modify **Nat.fs only**.

Do NOT modify:

- ExternalInterface.fs
- PacketRouter.fs
- Service.fs
- Any client-side code

Do NOT change public module names or function signatures used by other code.

---

## Required Changes in Nat.fs

### 1) Change `translateInbound` decision for `dstIp <> externalIp`

Find `translateInbound` in `Nat.fs`.

You likely have a block similar to:

```fsharp
let translateInbound (externalIp: uint32) (packet: byte[]) : byte[] option =
    ...
    let dstIp = readUInt32 packet 16
    if dstIp <> externalIp then
        Some packet
    else
        ...
```

Replace it with:

```fsharp
let translateInbound (externalIp: uint32) (packet: byte[]) : byte[] option =
    ...
    let dstIp = readUInt32 packet 16

    // Only handle packets addressed to our public IP.
    // Everything else is not part of VPN NAT and must be ignored.
    if dstIp <> externalIp then
        None
    else
        ...
```

### 2) Keep existing TCP/UDP NAT inbound mapping logic unchanged

Inside the `else` branch (where `dstIp = externalIp`), keep the existing logic that:

- Identifies protocol TCP (6) or UDP (17)
- Extracts `dstPort` (which is the NAT external port)
- Looks up the mapping in `portToMapping`
- Rewrites `dstIp` to internal client IP (e.g. 10.66.77.2)
- Rewrites `dstPort` to the original internal port
- Recomputes checksums
- Returns `Some translatedPacket`

If there is no mapping, continue to return `None` and keep the existing log message (if any).

### 3) Do NOT add throttled logs

Per requirement: **no throttled trace** right now.

- Keep any existing logs as-is, but do not add new “once per second” / throttling logic.
- Do not dump packet bodies.

---

## Expected Outcome

After this change:

- The raw socket receive loop may still see lots of external traffic, but
- Only traffic destined to `externalIp` is even considered for NAT inbound rewriting, and
- Only traffic matching a known NAT mapping will be injected into the VPN adapter.

This should:

- Reduce noise in the tunnel,
- Prevent injecting unrelated traffic into the VPN,
- Make DNS debugging possible.

---

## Deliverables

Claude Code must output:

1. A patch to `Nat.fs` applying the change above.
2. No modifications to any other files.
3. No new throttling code.

---

## Next Test

After applying and rebuilding:

Run on the client:

```powershell
nslookup google.com 8.8.8.8
```

Then inspect logs for a clean sequence:

- Client shows IPv4 UDP/53 packet from 10.66.77.2 to 8.8.8.8
- Server shows `NAT OUT` creating a mapping for that UDP flow
- Server shows inbound UDP reply to `externalIp:extPort`
- Server shows `NAT IN` mapping back to 10.66.77.2:originalPort
- Client receives a DNS response packet

If DNS still times out after this fix, then we will inspect client `Tunnel.fs` and/or interface DNS routing.

