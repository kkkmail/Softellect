# vpn_gateway_spec__050__android_resilience_reconnect.md

## Goal

Implement **automatic reconnection resilience** for the Android VPN client so that when the VPN server is stopped and later restarted, the Android client:

- **detects loss of connectivity** (via ping/heartbeat),
- transitions **Connected → Reconnecting**,
- **keeps traffic blocked** (no leak outside VPN),
- **re-queries network information before re-authentication**,
- re-authenticates and obtains a **new session id** (server restart invalidates old sessions),
- returns to **Connected** automatically when healthy again,
- and correctly surfaces the **last error** (must not remain `None` when exceptions occur).

The Android implementation must follow the **same logic and numbers** already used by the **Windows client**, for now.

## Scope

Android codebases involved:

- `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\`
- `C:\GitHub\Softellect\Vpn\AndroidClient\`

Reference implementation (source of truth for reconnection logic and numbers):

- `C:\GitHub\Softellect\Vpn\` (Windows client / shared code)
- Windows client app: `C:\GitHub\Softellect\Apps\Vpn\VpnClient\`

## Mandatory constraints for Claude Code (CC)

1. **Do not copy/paste Windows code into Android.**
   - CC must **find the Windows reconnection logic** and **reuse/adapt it** in a minimal, platform-appropriate way.
   - “Reuse/adapt” means: same state machine semantics, same timers/backoff thresholds, same retry cadence, same success/failure conditions — but implemented with Android-side primitives as needed.

2. **If CC cannot find the Windows logic or the exact constants/thresholds, CC must stop and ask.**
   - Do not invent numbers.
   - Do not approximate.
   - Do not “make it configurable” as a workaround.

3. **If CC hits a platform discrepancy that cannot be resolved cleanly (without hacks / without code duplication), CC must stop and ask.**
   - Do not force it with “conditional compilation” unless it is already part of the existing architecture and is clearly the intended mechanism.

4. **Naming rule for new code:** use **camelCase** for newly introduced functions/members/locals.
   - Do **not** refactor existing PascalCase names even if they are inconsistent.
   - Only keep new additions camelCase.

5. Keep the change set tightly scoped to resilience/reconnect and last-error correctness. No unrelated refactors.

## Behavioral requirements

### 1) Connectivity detection (UDP data plane)

Because the data plane is UDP, the client cannot reliably infer “send failure” as a connection failure.

Therefore:
- The **ping/heartbeat mechanism** is the authoritative liveness signal.
- The Android client must use the **same ping/heartbeat failure/success interpretation** as Windows.

### 2) State machine and UI truthfulness

When liveness fails:
- Transition **Connected → Reconnecting** immediately once the Windows-equivalent failure condition triggers.
- While in **Reconnecting**, the UI must NOT report “Connected”.
- When reconnection succeeds, transition back to **Connected**.

### 3) Traffic blocking during reconnect (no leak)

While in **Reconnecting**:
- **Traffic must remain blocked** (must not leak to the physical network).
- The VPN must not silently fall back to non-VPN routing.

Implementation note:
- The VPN service may remain running; the key icon may remain, which is acceptable.
- However, packet forwarding must not resume until reconnection is complete.

### 4) Reconnection sequence (order is strict)

When entering **Reconnecting**, the client must execute the following order (repeat according to Windows logic/backoff):

1. **Re-query network information BEFORE any authentication attempt**:
   - `getNetworkType`
   - `getPhysicalInterfaceName`
   - `getPhysicalGatewayIp`

   Rationale: disconnect may be caused by network change (Wi‑Fi/cellular/gateway/interface changes). Authentication must use the current network.

2. After network info refresh:
   - re-authenticate using the existing auth mechanism,
   - obtain a **new session id**,
   - reinitialize any session-tied state as Windows does,
   - then validate health as Windows does (e.g., ping success / session established / receive loop ready).

3. Only after successful reconnection validation:
   - transition to **Connected**,
   - resume forwarding.

### 5) Server restart invalidates sessions

After a server restart:
- the old session id must be treated as invalid,
- reconnection must obtain a new session id (same as Windows behavior).

### 6) Last error must be correct (fix “None” bug)

Currently the Android client’s “last error” remains `None` even though exceptions appear in logs.

Required fix:
- Whenever an exception occurs that contributes to:
  - a ping failure decision,
  - a reconnect attempt,
  - or a reconnect failure,
  the client must set/update the “last error” state to reflect the most recent meaningful failure (as Windows does).

This “last error” must be visible in the UI status (e.g., in the reconnecting state) and must not be overwritten back to `None` until a successful reconnection (unless Windows logic dictates otherwise).

## Implementation guidance (authoritative, no options)

### A) Locate and mirror Windows logic

CC must:

1. Identify the Windows client reconnection implementation:
   - where ping/heartbeat is evaluated,
   - where the reconnect state is entered,
   - where backoff/cadence/threshold constants are defined,
   - where re-authentication and new session acquisition is performed,
   - where success transitions back to Connected.

2. Implement the Android equivalent by **mapping**:
   - same states,
   - same thresholds and cadence,
   - same transition conditions,
   - same “traffic blocked while reconnecting” semantics,
   - same “error propagation” semantics.

### B) Android integration points

CC must integrate the reconnection logic with the Android client’s existing connect/disconnect pipeline:

- The manual “Disconnect / Connect” button must remain functional.
- Automatic reconnection must not require user interaction.
- Network info refresh must happen inside the reconnect loop **before** auth (every reconnect attempt, per Windows logic).

### C) Clipboard/log copy limitation

The issue where “Copy log” only copies a portion of the log (likely BlueStacks/Android clipboard limitation) is **out of scope** for spec 050. Do not modify logging UX here.

## Acceptance criteria (must pass)

Using the test scenario:

1. Start server.
2. Start Android client.
3. Connect; key icon appears; browsing works through VPN.
4. Stop server; wait any duration.
5. Client transitions to **Reconnecting** automatically (must not still claim Connected).
6. While reconnecting, traffic is **blocked** (no leak / no working internet outside VPN).
7. Start server again.
8. Client automatically re-queries network info, re-authenticates, gets a new session id, and returns to **Connected**.
9. Browsing works again without user clicking disconnect/connect.
10. When errors/exceptions occur during the failure window, “last error” is not `None` and reflects the failure, and clears/updates according to Windows behavior after recovery.

## Deliverables

CC must provide:

- A short summary of which Windows code paths/constants were used as the reference.
- A concise list of modified files under:
  - `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\`
  - `C:\GitHub\Softellect\Vpn\AndroidClient\`
- Confirmation that no Windows code was copy/pasted verbatim, and that camelCase was used for new additions only.
