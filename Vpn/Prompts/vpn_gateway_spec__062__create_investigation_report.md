# vpn_gateway_spec__062_create_investigation_report.md

## Purpose

This document instructs **Claude Code (CC)** to perform an **investigative analysis only** (NO CODE CHANGES) to determine why **Windows client → Linux server UDP data plane** does not function correctly, while:

- Windows client → Windows server ✅ works
- Android client → Windows server ✅ works

The goal is to produce a **diagnostic report with findings and recommendations**, not to implement fixes.

---

## Scope (STRICT)

CC **MUST**:
- Read existing code and logs
- Correlate runtime behavior with source code
- Identify *where* and *why* behavior diverges on Linux
- Propose likely root causes and remediation options

CC **MUST NOT**:
- Modify any code
- Propose speculative fixes without log/code correlation
- Add new features or refactors

Output is **analysis + recommendations only**.

---

## Artifacts to Read (MANDATORY)

### 1. Server trace log (Linux)

```
C:\GitHub\Softellect\!Temp\vpn_log.txt
```

This log was captured with **TRACE logging enabled** while a Windows client attempted to communicate with a Linux server.

---

### 2. Client trace log (Windows)

```
C:\GitHub\Softellect\!Temp\-a__20260115__001.txt
```

This log was captured from a Windows client attempting to communicate with the Linux server with **TRACE logging enabled**.

---

## Entry Points (START HERE)

### Server (Linux)

```
C:\GitHub\Softellect\Apps\Vpn\VpnServerLinux\Program.fs
```

CC must:
- Start from `main`
- Trace initialization of:
  - WCF auth plane (HTTP)
  - UDP push / data plane
  - ExternalGateway (Linux-specific)
  - PacketRouter / NAT pipeline

---

### Client (Windows)

```
C:\GitHub\Softellect\Apps\Vpn\VpnClient\Program.fs
```

CC must:
- Start from `main`
- Trace:
  - Auth handshake
  - Transition from HTTP → UDP
  - Session establishment
  - First UDP send / expected receive

---

## Known Facts (DO NOT RE-DISCOVER)

- Linux server **starts correctly**
- HTTP (CoreWCF over HTTP, NOT HTTPS) auth + version check **succeeds**
- UDP sockets are **bound and listening** on Linux
- Linux server logs show **no fatal errors or crashes**
- Oversized UDP packet warnings **are known and NOT the root cause**
- Same client code works against Windows server

CC should treat the above as **constraints**, not hypotheses.

---

## Investigation Objectives

CC must determine:

1. **Exact point of divergence** between:
   - Windows-server execution path
   - Linux-server execution path

2. Whether the issue is due to:
   - Linux-specific socket semantics
   - Raw socket vs datagram socket interaction
   - Packet routing / NAT translation differences
   - Threading / async receive loop behavior
   - Silent packet drops due to logic (not OS)

3. Whether packets are:
   - Received but ignored
   - Received and misclassified
   - Sent but malformed
   - Never routed back to the client

---

## Required Methodology

CC must:

- Walk the **code path end-to-end**, not jump to conclusions
- Annotate **which log lines correspond to which code paths**
- Compare Windows vs Linux behavior **line-by-line where relevant**
- Identify assumptions in code that hold on Windows but not Linux

---

## Output Requirements (STRICT)

CC must produce **ONE markdown file only** with:

### 1. Executive Summary
- One-page maximum
- Clear statement of the most likely root cause(s)

### 2. Timeline Correlation
- Map key log events (client + server) to code locations

### 3. Divergence Analysis
- Explicit comparison: Windows server vs Linux server behavior

### 4. Findings
- Ordered list of concrete findings
- Each finding must reference **both code and logs**

### 5. Recommendations
- Ranked by confidence
- Clearly marked as:
  - "High confidence"
  - "Medium confidence"
  - "Low confidence / needs validation"

NO code. NO patches. NO experiments.

---

## Deliverable

**File name (EXACT):**

```
C:\GitHub\Softellect\Vpn\Prompts\vpn_gateway_spec__062__report__01.md
```

This file must be suitable for direct archival in the repo.

---

## Final Note to CC

This is **not** a greenfield design task.
This is **forensic debugging** across OS boundaries.

Assume the code mostly works.
Find **why it stops working only on Linux**.
Ask questions if something is not clear.

