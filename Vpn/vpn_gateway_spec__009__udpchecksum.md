# vpn_gateway_spec__009__udpchecksum.md

## Why this iteration

DNS still fails after adding DNS proxy logic. With the current server code, the most suspicious “hard bug” is in `Nat.fs` checksum implementation:

### Critical issue in `transportChecksum`

`transportChecksum` currently always skips the 16-bit word at:

```fsharp
if i = ihl + 16 then () // skip checksum field itself
```

That is only correct for **TCP** (checksum field at TCP offset 16).

For **UDP**, the checksum field is at UDP offset **6**, i.e. `ihl + 6`.

So for UDP packets:

- You zero the UDP checksum field at `ihl + 6` (good),
- Then `transportChecksum` skips `ihl + 16` (wrong) — which is inside the UDP payload,
- Result: UDP checksum is incorrect.

This breaks:
- NAT for UDP (including DNS queries/replies),
- any server-generated UDP packet that uses `updateTransportChecksum`,
- likely your DNS proxy reply packets if it uses the same helper.

This iteration fixes UDP checksum computation correctly.

**Important:** Konstantin will build and test. Claude Code must keep changes small and auditable.

---

## Scope

Modify **only**:

- `Vpn/Server/Nat.fs`

Optional (nice-to-have but small):
- add a tiny internal self-check function in Nat.fs (no test project changes)

Do **NOT** modify other files unless absolutely necessary.

No throttled logging.

---

## Required changes

### 1) Fix checksum skip offset in `transportChecksum`

In `Nat.fs`, locate:

```fsharp
let private transportChecksum (buf: byte[]) (proto: Protocol) =
    ...
    let mutable i = ihl
    while i < ihl + segLen do
        if i = ihl + 16 then
            // skip checksum field itself (we'll write it later)
            ()
        else
            ...
        i <- i + 2
```

Replace this with a protocol-correct skip offset:

```fsharp
let private transportChecksum (buf: byte[]) (proto: Protocol) =
    let ihl = headerLength buf
    let totalLen = int (readUInt16 buf 2)
    let segLen = totalLen - ihl

    let checksumOffset =
        match proto with
        | Tcp -> ihl + 16
        | Udp -> ihl + 6
        | Other _ -> -1

    ...
    let mutable i = ihl
    while i < ihl + segLen do
        if i = checksumOffset then
            // skip checksum field itself
            ()
        else
            if i + 1 < ihl + segLen then
                add16 (readUInt16 buf i)
            else
                let last = uint16 buf[i] <<< 8
                add16 last
        i <- i + 2
```

Notes:
- Keep the existing “pseudo-header” summation.
- Keep the existing “fold carries” logic.
- For `Other _` you can just not skip anything (or skip -1 never matches).

### 2) Ensure `updateTransportChecksum` uses `transportChecksum` unchanged

`updateTransportChecksum` already zeroes the checksum field at the right place for UDP (`ihl+6`) and TCP (`ihl+16`).
After fixing `transportChecksum`, this will finally work.

No other logic changes required.

---

## Optional: Add a tiny local self-check helper (no new projects)

Add an internal function (private) in `Nat.fs` that can be called manually during debugging:

```fsharp
let private verifyUdpChecksum (packet: byte[]) : bool =
    let ihl = headerLength packet
    let proto = getProtocol packet
    match proto with
    | Udp ->
        let expected = transportChecksum packet Udp
        let actual = readUInt16 packet (ihl + 6)
        expected = actual
    | _ -> true
```

Do NOT call it from hot loops; it’s for manual debugging only.

---

## Expected outcome

After this fix:

- UDP packets processed by NAT will have valid UDP checksums.
- DNS (UDP/53) has a real chance of working end-to-end.
- If DNS still fails after this fix, the next most likely causes are:
  - DNS proxy reply packet construction (if it bypasses Nat helpers),
  - raw socket receive direction filtering,
  - client adapter DNS settings.

---

## What I will do after you implement

I will re-run the VPN and test:

```powershell
nslookup example.com 10.66.77.1
```

and then share logs again.

---

## If you discover DNS proxy does NOT use Nat.updateTransportChecksum

Stop and tell me which file/function you found that builds the DNS reply packet so we can verify its checksum handling.

