# vpn_gateway_spec__058__vpn_tray_ui_admin_app

**Authoritative specification for Claude Code (CC)**

This document defines *exactly* what must be implemented. No extra features. No guesses. No refactors beyond what is explicitly required.

---

## Global rules (MANDATORY)

1. **Language & style**
   - F# only
   - camelCase for **all new identifiers** (methods, functions, records, fields, modules, files where applicable)
   - follow existing project conventions exactly

2. **Do not invent infrastructure**
   - CC **must explore the existing code** and reuse:
     - CoreWCF hosting patterns
     - appsettings loading/binding
     - existing access-info / endpoint configuration patterns
   - Do **not** introduce new frameworks, helpers, or abstractions

3. **New functionality must live in new files**
   - WCF admin interface → new files
   - Tray UI implementation → new files
   - Existing files may only be *minimally* modified to wire things together

4. **Android client must NOT be modified**
   - Path: `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\`

---

## Target projects & locations

### Admin app (tray UI mode added)
- Path: `C:\GitHub\Softellect\Apps\Vpn\VpnClientAdm\`
- Uses **Argu**
- New UI mode enabled via:
  - **AltCommandLine = "-tray"**

### VPN client service
- Entry point:
  - `C:\GitHub\Softellect\Vpn\Client\Program.fs`
- Hosted service implementation:
  - `C:\GitHub\Softellect\Vpn\Client\Service.fs`

### Service configuration
- File:
  - `C:\GitHub\Softellect\Apps\Vpn\VpnClient\appsettings.json`

---

## Required service-side changes

### 1. Decouple VPN start/stop from service lifetime

Current behavior:
- VPN starts/stops automatically with `IHostedService.StartAsync` / `StopAsync`

Required behavior:
- VPN **must NOT automatically start** on service startup
- VPN start/stop is controlled **only** by:
  - `AutoStart` config setting
  - admin WCF calls (`startVpn()`, `stopVpn()`)

### 2. Add AutoStart setting (SERVICE ONLY)

In `VpnClient/appsettings.json`, add:

- `AutoStart : bool`

Behavior:
- `AutoStart = true`  → VPN starts during service startup
- `AutoStart = false` → VPN remains stopped until explicitly started via admin API

Admin console **does not** read or control this setting.

---

## Admin ↔ Service WCF interface

### Contract (EXACT, no additions)

All method names **must be camelCase**:

- `getStatus()`
- `startVpn()`
- `stopVpn()`

No extra methods. No polling helpers. No callbacks.

### Status semantics

- Returned status must map directly to the **Android client states**
- Windows UI **must not invent new VPN states**

Windows UI adds **one UI-only state**:
- `ServiceNotRunning`
  - Used when WCF endpoint is unreachable
  - Service does NOT need to return this

---

## Admin communication configuration

Add a configuration section **similar to `VpnServerAccessInfo`** that defines:

- How the **admin app connects to the service**
- How the **service hosts the admin endpoint**

Rules:
- CC must locate `VpnServerAccessInfo`
- Mirror its structure and usage pattern
- Bind it via the same configuration mechanism already used in the solution

No hard-coded endpoints.

---

## Admin app tray UI mode

### Activation

- Enabled via Argu option:
  - `AltCommandLine = "-tray"`

### Single instance

- Enforced **only** in tray UI mode
- CLI/admin mode remains multi-instance

---

## Tray behavior (STRICT)

### Startup

1. Create tray icon
2. Call `getStatus()`
3. Display returned state
4. If service unreachable → display `ServiceNotRunning`

### Hover behavior

- On mouse hover over tray icon:
  1. Call `getStatus()`
  2. Keep displaying **last known state** until response arrives
  3. Update display when response is received

### Click behavior (single "power" action)

- If current state == "connected" (as defined by Android):
  - Call `stopVpn()`
- Else:
  - Call `startVpn()`

After command completes:
- Call `getStatus()` once
- Update UI

### Right-click menu

- Must contain exactly one item:
  - **Exit**

---

## Error display

- If status includes an error:
  - Show last error text in tray tooltip
- No logs
- No log viewer
- No admin console window

---

## Explorer restart handling

- Detect taskbar recreation
- Re-create tray icon when Explorer restarts
- No polling loops

---

## Explicit non-goals (DO NOT IMPLEMENT)

- ❌ Auto-start UI logic
- ❌ Settings UI
- ❌ Log viewers
- ❌ Periodic polling
- ❌ Background timers
- ❌ Android changes
- ❌ Extra WCF methods
- ❌ State machines different from Android

---

## Summary (for CC)

You are implementing **exactly one thing**:

> A tray-based UI mode for the existing admin app, controlled by `-tray`, which shows VPN state, toggles VPN on click, queries status only on startup/hover/click, and communicates with the service via a minimal WCF admin interface.

Nothing more.

