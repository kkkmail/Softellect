# VPN Gateway – Milestone 002  
## UDP Push Dataplane Reset & Failure Analysis Summary

**Scope:**  
This milestone documents the outcome of the debugging session that led to the decision to **delete all legacy UDP and WCF tunnel code** and keep **only**:
- WCF **authentication** channel
- **UDP push dataplane** (client ↔ server)

No fixes are implemented yet. This is a *reset point* with concrete reasons.

---

## 1. Observed Symptoms

- Client successfully:
  - Authenticates via WCF
  - Brings up WinTun adapter
  - Sends UDP push traffic (confirmed by client stats)
- Server:
  - Receives UDP traffic
  - Push registry is created
  - Push stats intermittently advance
- Yet:
  - Traffic becomes unreliable
  - Pings time out
  - Behavior degrades after startup
  - No consistent error signal explains failure

---

## 2. Root Cause Class (High Confidence)

The system is **architecturally inconsistent**, not just buggy.

The codebase simultaneously supports:
- WCF tunnel
- Legacy UDP tunnel
- UDP push dataplane

…but **only UDP push is actually used**.

This creates hidden cross-coupling and failure modes even when “unused”.

---

## 3. Critical Failure Mechanism Identified

### Combined UDP Server Receive Loop Can Block Itself

The server uses a *combined* UDP receive loop:

- If datagram matches **push header** → handled inline
- Else → routed into **legacy work channel**

However:
- All legacy UDP workers are **disabled**
- The legacy work channel is **bounded**
- Once that channel fills:
  - `WriteAsync` blocks
  - The **entire UDP receive loop stalls**
  - **Push traffic stops being processed**

This can be triggered by:
- Random internet UDP noise
- Malformed / partial packets
- Any packet that fails push header parsing

This alone is sufficient to explain:
- “Works at first, then dies”
- “Unreliable communication”
- No obvious error logs

---

## 4. Push Session / Registry Model Contamination

Even inside “push-only” paths:

- Push sessions and legacy sessions coexist
- Activity tracking updates the **wrong registry**
- Lifetime / liveness logic is split across two models

This creates:
- Silent inconsistencies
- Session logic that *appears* valid but is internally corrupted

---

## 5. Backpressure & Silent Drops

- Server push queues are bounded
- Enqueue failures are not always checked
- Packet loss can occur without logs

This does **not** explain total stalls, but contributes to unreliability.

---

## 6. Decision Taken

**Delete immediately:**
- Legacy UDP tunnel
- WCF tunnel dataplane
- Combined UDP server logic
- Any registry / channel / worker related to non-push traffic

**Keep only:**
- WCF authentication service
- Dedicated UDP push server
- Dedicated UDP push client
- Single push registry model
- One receive loop → one dataplane → no branching

---

## 7. Next Milestone (Not Implemented Yet)

- Clean push-only server:
  - No legacy branches
  - No bounded channels without consumers
  - Push-only protocol surface
- Deterministic packet flow
- Explicit backpressure handling
- Explicit lifecycle state machine

---

**Status:**  
🛑 Hard reset justified.  
This milestone marks the end of mixed-mode tunneling.

