# TODO 001 — REDUCE UDP PUSH HEADER SIZE (DEFERRED)

THIS IS A **DEFERRED DESIGN TODO**.  
NO CODE CHANGES ARE REQUIRED OR EXPECTED NOW.

THIS FILE EXISTS SO THE DESIGN INTENT IS NOT LOST.

---

## CURRENT STATE

File (at time of writing):
- `C:\GitHub\Softellect\Vpn\Core\UdpProtocol.fs`

Current constant:
- `PushHeaderSize = 28` bytes

This value is **artificially large** and reflects legacy / over-engineered fields that are no longer needed.

---

## OBSOLETE / REMOVABLE FIELDS

The following fields can be removed or compressed:

1. **Version**
   - Not needed anymore
   - Savings: **-1 byte**

2. **Flags**
   - Currently unused
   - Savings: **-2 bytes**

3. **ClientId**
   - Full GUID is unnecessary in push UDP
   - Client is already identified by session + endpoint
   - Only the **last IP byte** is sufficient if any identifier is still desired
   - Savings: **-15 bytes**

4. **Reserved**
   - No longer needed
   - Savings: **-2 bytes**

**Total removable size:**  
`1 + 2 + 15 + 2 = 20 bytes`

---

## COMMAND + PAYLOAD LENGTH ENCODING

Constraints:
- MTU is well below `2^12`
- Payload length easily fits in **12 bits**
- There are **only 3 commands** → fits in **2 bits**

Proposed encoding:
- Use **16 bits total**
  - Upper 4 bits: `command`
  - Lower 12 bits: `payloadLen`

This keeps alignment simple and avoids bit-level gymnastics.

Savings vs current layout:
- No increase (still 2 bytes)
- But replaces multiple fields with a single compact word

---

## RESULTING HEADER SIZE (TARGET)

Original:
- **28 bytes**

After removals:
- `28 - 20 - 1 = 7 bytes` (realistic target)

So the **true push UDP header can be ~7 bytes**, not 28.

---

## IMPORTANT NOTE

This optimization is **NOT REQUIRED RIGHT NOW**:
- Current performance is acceptable
- MTU is already managed
- No fragmentation/reassembly is implemented yet

This should be revisited **only if**:
- Throughput optimization becomes critical
- Packet coalescing is implemented
- Protocol is formally versioned/documented

---

## WHEN THIS TODO IS PICKED UP

- Re-evaluate the then-current `UdpProtocol.fs`
- Do NOT blindly apply this design if the protocol evolved
- Update header parsing, building, and size constants consistently
- Update MTU calculations accordingly

---

## FILE NAMING

This file intentionally starts with `!_todo__` to keep it visible and non-actionable.
