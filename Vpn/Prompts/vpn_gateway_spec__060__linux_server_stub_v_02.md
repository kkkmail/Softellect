# vpn_gateway_spec__060__linux_server_stub__V02

## Goal

Implement the **minimal** Linux server “flavor” that:

- Builds on Windows (developer machine) and can later be published for **AlmaLinux 9**.
- Starts successfully on Linux but **does nothing** (explicit stub behavior).
- Avoids architectural refactoring (no interfaces / DI redesign yet).
- Preserves backward compatibility for all existing Windows services and projects.

This spec is for **implementation**. It intentionally limits scope.

---

## CRITICAL BUILD RESTRICTION (READ CAREFULLY)

- **CC MUST NOT attempt to build the solution.**
- **CC MUST NOT run `dotnet build`, `dotnet test`, or any variant.**
- The solution is known to be **non-buildable in a clean environment** due to .NET / SDK / tooling issues.
- The user will build locally and provide errors if needed.
- CC must rely **exclusively on static analysis**:
  - project files
  - references
  - conditional compilation
  - code inspection

This restriction is intentional and non-negotiable.

---

## Hard constraints

### 1) Conditional compilation symbols

- Allowed symbols: `ANDROID`, `LINUX`
- **Disallowed symbol: `WINDOWS`**

Windows behavior must remain the **default path** and be guarded only via `#if LINUX` / `#if ANDROID` exceptions.

Example pattern:
- `#if LINUX` → Linux stub
- `#else` → existing Windows behavior

### 2) F# naming (MANDATORY)

- **Use camelCase for any new F# identifiers.**
- **Use camelCase for any new F# identifiers.**
- Do **NOT** rename existing identifiers even if they violate conventions (already-broken names stay unchanged).

### 3) Discrepancies

- If any conflict, ambiguity, or mismatch is discovered between this spec and repository reality:
  - **STOP**
  - **ASK THE USER**
  - Do NOT guess or silently “fix” things.

### 4) Copy / paste

- Copy/paste is explicitly disallowed.
- All changes must be intentional and minimal.

---

## Scope

### In-scope folders

- `C:\GitHub\Softellect\Vpn\`
- `C:\GitHub\Softellect\Apps\Vpn\`

### Out-of-scope for movement

- Anything outside the two folders above must **not be moved**.
- Such code may only be linked and/or conditionally compiled.

---

## Deliverable (post-implementation expectations)

### 1) Linux server project

Create a new project folder:

```
C:\GitHub\Softellect\Apps\Vpn\VpnServerLinux\
```

The project must:
- Target `net10.0`
- Define the `LINUX` compilation symbol **for this project only**
- Be added to the **main solution** (preferred, unless a hard blocker is found)

### 2) Linux server behavior

- The Linux server must start successfully.
- Behavior is **explicitly stubbed**:
  - No packet routing
  - No TUN/TAP
  - No Wintun
- The server must clearly log that it is running in **LINUX STUB MODE** (or equivalent explicit message).

Whether it stays running or exits cleanly should follow the least disruptive pattern consistent with existing server hosting.

---

## Implementation tasks

### Task A — Linux server entrypoint (stub)

- Create a Linux-specific server entrypoint (`Program.fs` or equivalent) under `VpnServerLinux`.
- Structure should mirror the Windows server **only at a high level** (configuration, logging, DI wiring if reused).
- Anything that requires Windows-only APIs must be stubbed out or excluded under `#if LINUX`.

---

### Task B — Make `Softellect.Wcf.Program` Linux-safe

Current issue:
- `.UseWindowsService()` is hardcoded inside the Wcf composition root.

Required change:
- Preserve existing Windows behavior by default.
- When `LINUX` is defined, **do not call** `.UseWindowsService()`.

Rules:
- Use `#if LINUX` / `#else` / `#endif`.
- Do **NOT** introduce a `WINDOWS` symbol.

---

### Task C — Minimal Interop split (Windows-only isolation)

Known blocker:
- `C:\GitHub\Softellect\Vpn\Interop\` contains Wintun-based native Windows code.

For v060, perform the **minimal structural split** needed so Linux server code does **not reference** Wintun.

Recommended structure (names may be adapted to repo conventions):

- `Interop.Common`
  - Platform-neutral types
  - Shared helpers
- `Interop.Windows`
  - Wintun wrapper
  - All Windows-only P/Invoke / native interop

Rules:
- Windows server continues to use Windows Interop unchanged.
- Linux server references **only** the common part.
- No interface abstraction yet (that is a later phase).

---

### Task D — Guard Windows-only server code paths

Some server modules contain Windows-only logic (e.g. Wintun usage, SIO_RCVALL, etc.).

For v060:
- Identify the **minimum** set of files that block Linux compilation.
- Apply one of:
  - Conditional compilation using `#if LINUX` to provide a stub implementation
  - File split (`File.fs` + `File.Linux.fs`) compiled conditionally

Rules:
- Windows behavior remains the default (`#else`).
- Linux behavior must be explicit stub (log / throw / no-op).

---

### Task E — Solution integration (static only)

- Add `VpnServerLinux` to the main solution.
- Do **NOT** attempt to build.
- Validate correctness via static inspection only.

---

## Acceptance checklist

- `VpnServerLinux` exists and is part of the main solution.
- No `WINDOWS` compilation symbol exists anywhere.
- `LINUX` symbol is used consistently and only where needed.
- `Softellect.Wcf.Program` no longer invokes `.UseWindowsService()` under `LINUX`.
- Linux server code does not reference Wintun or Windows-native APIs.
- All Linux behavior is explicitly stubbed and clearly logged.
- F# camelCase rule followed for all new identifiers.
- CC asked questions if any discrepancy was found.

---

## Final reminder to CC (repeat)

- **DO NOT BUILD THE SOLUTION.**
- **DO NOT TRY TO FIX BUILD ERRORS.**
- Static analysis only.
- Ask when in doubt.

---

## Notes

- This phase intentionally avoids clean abstractions.
- Interface-based isolation of Wintun and real Linux TUN/TAP support will be handled in a later spec (v061+).

