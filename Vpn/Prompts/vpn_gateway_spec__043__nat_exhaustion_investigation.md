# vpn_gateway_spec__043__nat_exhaustion_investigation.md

## Purpose

This document defines an **investigation-only task** for Claude Code (CC).  
The goal is to analyze the VPN server codebase and **identify the root cause(s)** of the following runtime error observed after long uptimes:

```
NAT: no free external ports/ids
```

**Important:**  
CC must **NOT modify any code**.  
CC must **NOT propose fixes**.  
CC must **ONLY analyze and report findings** based strictly on the existing code.

---

## Scope (Strict)

### INCLUDED
- NAT implementation and lifecycle
- Allocation, tracking, and release of:
  - external ports
  - external IDs (if distinct from ports)
- UDP NAT handling
- Cleanup / expiration logic
- Concurrency and shared-state handling
- F# NAT logic
- Any C# interop that participates in NAT allocation or lifecycle

### EXCLUDED (Do NOT mention or analyze)
- Packet size, MTU, PushMtu
- Fragmentation or oversized packets
- Performance tuning
- Any changes to protocol behavior

---

## Code Locations to Examine

CC **must examine both folders in full**:

```
C:\GitHub\Softellect\Vpn\
C:\GitHub\Softellect\Apps\Vpn\
```

The NAT logic is primarily in **F#**, but **C# interop code must also be reviewed** where it affects NAT state, allocation, or cleanup.

---

## Observed Runtime Behavior

- VPN server runs normally for many hours (~1 day).
- Two clients connected.
- Very low traffic.
- Eventually server logs repeated errors:
  ```
  NAT: no free external ports/ids
  ```
- Restarting the server (without stopping clients) immediately resolves the issue.

This strongly suggests a **logical exhaustion** rather than real demand.

---

## Required Investigation Questions

CC must answer **all** of the following, with references to concrete code locations.

### 1. Exhaustion Detection
- Where exactly is the error `"NAT: no free external ports/ids"` generated?
- What function/module throws or logs it?
- What condition must be true for this error to occur?

### 2. Definition of “external ports/ids”
- What are “external ports” and/or “external ids” in this implementation?
- Are they:
  - UDP source ports?
  - Internal NAT mapping identifiers?
  - A combined abstraction?
- Are ports and ids distinct pools or a single pool?

### 3. Pool Size and Limits
- What is the total pool size?
- What ranges are used?
- What exclusions apply (reserved ports, blocked ranges, etc.)?
- Are there off-by-one or wraparound risks?

### 4. Allocation Path
- Where are external ports/ids allocated?
- What data structures track allocated vs free?
- Is allocation:
  - eager or lazy?
  - per-flow, per-packet, or per-session?
- Is allocation atomic with respect to concurrency?

### 5. NAT Key Stability
- What key uniquely identifies a NAT mapping?
- Does the key include only stable flow properties?
- Is there any risk of unbounded key growth (e.g. per-packet uniqueness)?

### 6. Release / Expiration
- Where are NAT mappings released or expired?
- Is there:
  - a TTL?
  - an idle timeout?
- How is `lastSeen` (or equivalent) updated?
- Is cleanup:
  - timer-driven?
  - loop-driven?
  - conditional?
- Is the cleanup mechanism guaranteed to run?

### 7. Failure and Exception Paths
- Are there code paths where:
  - allocation succeeds,
  - but subsequent logic fails,
  - and release never happens?
- Pay special attention to:
  - try/with blocks,
  - early returns,
  - dropped packets,
  - queue overflows.

### 8. Concurrency Safety
- What shared mutable structures are used?
- Are they:
  - protected by locks?
  - concurrent collections?
- Are there race conditions between:
  - allocation,
  - cleanup,
  - lookup?

---

## Deliverable

CC must produce a **report only**.

### If delivered as Markdown, filename MUST be exactly:
```
C:\GitHub\Softellect\Vpn\Prompts\vpn_gateway_spec__043__report__01.md
```

### Report must contain:
- Concrete code references (files, modules, functions).
- Clear explanation of **why NAT exhaustion can occur**.
- Distinction between:
  - confirmed issues,
  - plausible failure modes supported by code.
- No speculative fixes.
- No suggested refactors.
- No code snippets unless necessary to explain logic.

---

## Prohibited Actions

CC must NOT:
- modify any files except the report MD above
- add logging
- change configuration
- suggest fixes
- analyze unrelated subsystems
- mention MTU, packet size, or performance

This task is **analysis-only**.

---

## Completion Criteria

The task is complete when the report clearly explains:
- how the NAT pool is intended to work,
- how it can reach an exhausted state under low traffic,
- and which exact code paths or mechanisms allow that to happen.
