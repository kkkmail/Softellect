# vpn_gateway_spec__059__linux_server_migration_report__V01

## Purpose

Produce an **analysis-only report** (no code changes) that evaluates the feasibility and minimal-change path for running the **VPN server on Linux (AlmaLinux 9)**, starting from the existing **Windows service–based server**.

The report must:
- Walk the existing server entrypoint upward through dependencies
- Identify **Windows-only dependencies** and where they live
- Compare with the **Android client pattern** (linked files + conditional compilation)
- Recommend a **minimal structural split** (common / Windows / Linux) sufficient to get a Linux server flavor compiling and runnable

This is **NOT** an implementation task.

---

## Output (MANDATORY)

CC must produce **exactly one report file** at:

```
C:\GitHub\Softellect\Vpn\Prompts\vpn_gateway_spec__059__report__01.md
```

No other files must be created or modified.

---

## Target environment

- Linux distribution: **AlmaLinux 9**
- Runtime: **.NET 10**
- Migration scope: **SERVER ONLY**
- Client platforms (Windows, Android): **must remain working and unchanged**

---

## Starting point (entrypoint)

Windows server entrypoint:

```
C:\GitHub\Softellect\Apps\Vpn\VpnServer\Program.fs
```

Proposed Linux server location (for analysis only):

```
C:\GitHub\Softellect\Apps\Vpn\VpnServerLinux\
```

---

## Known constraints & assumptions

- All current server projects target `net10.0` (NOT `net10.0-windows`).
- Compiler currently does NOT enforce OS correctness.
- Minimal-change approach is required.
- Large refactoring (clean DI boundaries, removing composition roots from libraries, etc.) is **explicitly deferred**.
- Platform separation may use:
  - linked files
  - conditional compilation symbols (`WINDOWS`, `LINUX`, etc.)
  - platform-specific projects **only where strictly necessary**

---

## Known major issue (already identified)

```
C:\GitHub\Softellect\Vpn\Interop\
```

- This contains a **C# wrapper around the Wintun adapter**
- It mixes **client and server code**
- It is **Windows-only by nature**
- Linux server cannot reference Wintun

The report must treat this as a **mandatory split**, but only recommend structure — no refactoring yet.

---

## Scope boundaries

### In-scope

- Anything under:
  - `C:\GitHub\Softellect\Vpn\`
  - `C:\GitHub\Softellect\Apps\Vpn\`

### Out-of-scope (for movement)

- Any code **outside** the two folders above
- Such code may only be:
  - linked
  - conditionally compiled

---

## Required analysis steps (CC must follow)

### 1. Dependency walk

Starting from `VpnServer\Program.fs`:

- Walk **up and outward** through referenced projects/modules
- Identify:
  - where hosting is configured
  - where Windows service integration occurs
  - where CoreWCF is wired

Produce a **dependency chain diagram (textual)** in the report.

---

### 2. Windows dependency classification

For each Windows dependency encountered, classify it into one of the following buckets:

- Windows Service hosting (`UseWindowsService`, SCM assumptions)
- Native Windows interop (Wintun, P/Invoke)
- Windows-only networking / firewall / kernel assumptions
- Filesystem / identity / EventLog / registry (if any)

For each item, record:
- File(s)
- Project
- Whether it is **server-only**, **client-only**, or **mixed**

---

### 3. Android comparison

Analyze how platform separation is handled in:

```
C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\
```

Specifically:
- linked files
- conditional compilation patterns
- project structure

Then answer explicitly:

> Can the same pattern be applied to a **Linux server** with minimal disruption?

---

### 4. Linux server project proposal (analysis only)

Evaluate whether it is reasonable to:

- Add a **Linux server project** into the **main solution** (unlike Android, which has its own solution)

Provide a recommendation **with justification**, covering:
- build impact
- conceptual clarity
- maintenance cost

---

### 5. Interop split recommendation (server-first)

Propose a **minimal** structural split of Interop, for example:

- `Interop.Common`
- `Interop.Windows`
- (optional) `Interop.Linux` (may be stub-only initially)

For each proposed part, clearly state:
- what moves there
- what remains unchanged
- what Linux server would reference

NO code movement is to be done — analysis only.

---

### 6. Other necessary splits (if any)

If additional modules (e.g. Wcf, Server, Sys) contain **hardcoded Windows behavior**, CC must:
- identify them
- assess whether they require:
  - conditional compilation
  - file split
  - project split

Keep recommendations **minimal and incremental**.

---

## Final deliverables inside the report

The report **must end with**:

1. **Concrete recommendations** (bullet list)
2. **Proposed folder/project structure** (paths only, no code)
3. **Short-term actions** (to get Linux server compiling/running)
4. **Deferred refactor items** (explicitly marked as NOT part of this phase)

---

## Explicit prohibitions

- ❌ No code snippets
- ❌ No refactoring instructions beyond structure
- ❌ No MD files other than the single report
- ❌ No changes to existing projects

This task is strictly **analysis + recommendation**.

---

## Success criteria

The report is considered successful if:
- It clearly explains **why** Linux server is feasible with minimal changes
- It identifies **exact Windows blockers** precisely
- It proposes a **low-risk, Android-style adaptation path**
- It does **not** prematurely redesign the architecture

