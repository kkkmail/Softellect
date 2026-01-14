# vpn_gateway_spec__060__linux_server_stub__V01

## Goal

Implement the **minimal** Linux server “flavor” that:

- Builds on Windows (developer machine) and can be published for **AlmaLinux 9** later.
- Starts successfully on Linux but **does nothing** (stubbed behavior).
- Avoids architectural refactoring (no interface/DI redesign yet).
- Preserves backward compatibility for existing Windows services and projects.

This spec is for **implementation** (unlike v059 which was analysis).

## Hard constraints

1) Conditional compilation symbols:
- Allowed: `ANDROID`, `LINUX`
- Disallowed: `WINDOWS` (do not introduce this symbol anywhere)

2) F# naming:
- **Use camelCase** for any new F# identifiers you add.
- **Use camelCase** for any new F# identifiers you add.
- Do **not** rename existing identifiers even if they violate conventions (“already messed up” names stay as-is).

3) Discrepancies:
- If you find contradictions between this spec and the repo reality, **STOP and ASK** before proceeding.

4) Copy / paste:
- Copy/paste is disallowed. Implement changes intentionally.

## Scope

### In-scope folders
- C:\GitHub\Softellect\Vpn\
- C:\GitHub\Softellect\Apps\Vpn\

### Out-of-scope for movement
- Anything outside the two folders above must not be moved; only linked/conditionally compiled if needed.

## Deliverable (what must exist after changes)

1) New Linux server project folder:
- C:\GitHub\Softellect\Apps\Vpn\VpnServerLinux\

2) The Linux server project must:
- Build under `dotnet build` from the main solution.
- Provide a runnable entrypoint that starts and logs a clear message that Linux mode is stubbed / not implemented, then keeps running (or exits cleanly — choose the least disruptive behavior consistent with existing hosting patterns).

3) The main solution should include the Linux server project (confirm solution name in repo; add accordingly).

## Minimal change strategy

This phase is about **compiling and starting**, not about implementing Linux TUN/TAP.

The key is to ensure Linux builds do not pull in Windows-only native code (Wintun / WFP / Windows service hosting APIs).

## Tasks

### Task A — Add `VpnServerLinux` project (server stub)

1) Create a new project under:
- C:\GitHub\Softellect\Apps\Vpn\VpnServerLinux\

2) The project should:
- Target `net10.0`
- Define compilation constant `LINUX` for this project only (via project settings)
- Reference the same shared projects as needed to reuse server wiring, but only as far as they do not force Windows-only dependencies.

3) Entry point:
- Implement a Linux-specific `Program.fs` (or equivalent) that mirrors the Windows server startup *structurally* but uses Linux-safe paths.
- This Linux server may:
  - Use the same configuration loading patterns
  - Initialize DI and logging
  - Start network listeners only if they are already cross-platform
- But must **stub out** anything that requires:
  - Wintun
  - Windows-only interop
  - Windows service manager integration

Important: keep the behavior explicit (log “LINUX stub: not implemented yet”).

### Task B — Make `Softellect.Wcf.Program` Linux-safe without introducing `WINDOWS`

The report indicates `.UseWindowsService()` is hardcoded inside the Wcf composition root.

Requirement:
- Keep existing Windows behavior as the default.
- On Linux builds (when `LINUX` is defined), do not call `.UseWindowsService()`.

Implementation rule:
- Use `#if LINUX` … `#else` … `#endif` (or equivalent) so the Windows path is the `#else` branch.
- Do not introduce `WINDOWS` symbol.

### Task C — Split Interop so Linux does not reference Windows-native Wintun code

Known blocker:
- C:\GitHub\Softellect\Vpn\Interop\ contains a C# wrapper around Wintun and mixes client/server concerns.

For v060, do the **minimal structural split** needed so the Linux server project can compile without referencing Wintun.

Recommended minimal split (names may be adjusted to match repo conventions, but keep intent):
- Interop.Common: shared types/helpers that are platform-neutral and needed by both Windows server and Linux stub build
- Interop.Windows: Wintun-specific implementation and any Windows-only P/Invoke/native calls

Rules:
- Windows server keeps using Interop.Windows (directly or indirectly).
- Linux server must reference Interop.Common only (and must not load Wintun).

If you discover Interop is currently a single project used widely:
- Prefer creating new projects and updating references, rather than invasive refactors.

### Task D — Guard server Windows-only code paths with `LINUX` stubs (only where necessary)

The report mentioned Windows-only code in server modules (examples included SIO_RCVALL, PacketRouter/Wintun usage).

For v060:
- Identify the minimum set of files that prevent Linux build.
- For each, apply one of:
  - File-level split: `File.fs` (default/Windows) plus `File.Linux.fs` compiled only under `LINUX`
  - Conditional compilation blocks using `#if LINUX` to provide stub implementations

Rules:
- Use only `LINUX` and/or `ANDROID` symbols.
- Keep Windows code as the `#else` path.
- Stubs should be explicit (throw “not implemented on Linux yet” or return a safe no-op) and should not silently pretend to work.

### Task E — Solution integration and build validation

1) Add `VpnServerLinux` to the main solution (preferred).
2) Ensure `dotnet build` succeeds for the solution.
3) Ensure Windows server behavior remains unchanged.
4) Ensure Android solution/projects remain unchanged.

## Acceptance checklist

- `VpnServerLinux` exists under Apps\Vpn and builds.
- No new `WINDOWS` compilation symbol is introduced anywhere.
- `Softellect.Wcf.Program` no longer calls `.UseWindowsService()` when `LINUX` is defined.
- Linux build does not depend on Wintun / Windows-native assemblies.
- Any Linux stubs are explicit and safe (clear logging / exceptions).
- F# additions use camelCase; existing naming violations remain untouched.
- If any uncertainty arises, you ask before proceeding.

## Notes

- This is intentionally a stub-only phase. Do not implement Linux TUN/TAP yet.
- The next phase (v061+) can introduce proper abstractions (interfaces/DI) once Linux compilation and startup are established.
