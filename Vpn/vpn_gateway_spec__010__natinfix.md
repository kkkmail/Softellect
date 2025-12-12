# vpn_gateway_spec__010__fix_natin_ports_and_udp_checksum

## Working directory
Work only under:

`C:\GitHub\Softellect\Vpn\`

## What is failing
DNS over VPN times out.

The server logs show inbound packets arriving at the server public IP, but NAT inbound drops them with “no mapping for dstPort=...”. The logged “dstPort” equals the **remote source port**, which indicates NAT inbound is reading the wrong TCP/UDP header field (it reads source port instead of destination port).

Additionally, UDP checksum computation is incorrect because the checksum routine always skips the TCP checksum offset (IHL+16) even for UDP, producing invalid UDP checksums.

Both issues break return traffic (internet → client), including DNS replies.

## Required change set (no options)

### Files to modify
- `Vpn\Server\Nat.fs`

### 1) Fix NAT inbound port offset (TCP + UDP)
In `translateInbound`:

- When extracting the port used to look up the NAT table, read the **destination port**:
  - **Today (bug):** `let dstPort = readUInt16 packet ihl`
  - **Fix:** `let dstPort = readUInt16 packet (ihl + 2)`

- When rewriting the packet to the internal client, rewrite the **destination port**:
  - **Today (bug):** `writeUInt16 packet ihl internalPort`
  - **Fix:** `writeUInt16 packet (ihl + 2) internalPort`

Rationale: In TCP/UDP headers, source port is at offset 0, destination port is at offset 2.

### 2) Fix transport checksum calculation for UDP
In `transportChecksum`:

- Replace the hard-coded checksum skip location `ihl + 16` with the correct checksum offset based on protocol:
  - TCP checksum field offset = `ihl + 16`
  - UDP checksum field offset = `ihl + 6`

Implementation requirement:
- Compute `checksumOffset` for the given protocol and skip that 16-bit field while summing the transport segment.

Notes:
- Keep the logic that zeroes the checksum field in `updateTransportChecksum` before computing.
- Do not change behavior for protocols other than TCP/UDP.

### 3) Add minimal sanity logging for NAT IN mapping hit (required)
In `translateInbound`, when a mapping is found, log a single trace line including:
- protocol
- external destination port (the one used for lookup)
- rewritten internal destination `internalIp:internalPort`

Avoid logging full packet bytes.

### 4) Add a minimal deterministic self-test (required)
Add a small internal test function inside `Nat.fs` (no new test project required) that:
- Constructs a minimal IPv4+UDP packet byte array with:
  - any src IP
  - dst IP = externalIp
  - UDP src port = 12345
  - UDP dst port = 40000 (example NAT external port)
- Inserts a NAT table entry mapping `externalPort=40000` to an internal key (internal IP = 10.66.77.2, internalPort = 5353, proto=Udp).
- Calls `translateInbound externalIp packet` and asserts (via `failwith`) that:
  - output is Some
  - destination port in the output UDP header equals 5353
  - destination IP in the output equals 10.66.77.2

This is only to prevent regressions in the two offset mistakes.

The function can be named `internalSelfTest()` and should not run automatically at startup (the user can call it manually during debugging).

## Definition of done
- NAT inbound no longer treats remote source port as the mapping key.
- NAT inbound correctly rewrites destination port to the VPN client port.
- UDP checksum computation uses the UDP checksum field offset.
- DNS replies can traverse internet → server → client and the DNS query no longer times out in the VPN test run.
