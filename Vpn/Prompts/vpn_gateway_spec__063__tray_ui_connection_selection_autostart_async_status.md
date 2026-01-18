# vpn_gateway_spec__063__tray_ui_connection_selection_autostart_async_status

## Scope
This specification defines **mandatory** changes to the Windows tray UI for the VPN client. There are **no options** or alternatives. Claude Code (CC) must implement exactly what is specified here.

Affected primary file:
- `C:\GitHub\Softellect\Apps\Vpn\VpnClientAdm\TrayUi.fs`

Supporting file (helpers may be added, no direct JSON access):
- `C:\GitHub\Softellect\Vpn\Core\AppSettings.fs`

CC **must not** access `appsettings.json` directly under any circumstances. All persistence must go through existing `AppSettingsProvider` infrastructure.

All newly added F# identifiers **must use camelCase**. Existing names must not be renamed.

---

## 1. VPN Connection selection (tray context menu)

### 1.1 Menu naming and placement

- Add a context menu submenu named **"VPN Connection"** (singular).
- This submenu must appear **above** the `Exit` menu item.

This naming matches standard English usage in VPN software (e.g., OpenVPN uses “connection/profile” in singular form).

### 1.2 Menu content

- The submenu items are populated from `loadVpnConnections()`.
- Each VPN connection is represented by **one menu item**.
- Menu items behave as **radio buttons**:
  - exactly one item is checked at any time
  - the checked item corresponds to the currently selected VPN connection name

### 1.3 Display semantics

- Tray tooltip text when connected **must include both**:
  - VPN connection name
  - assigned IP

Format (exact):

```
<vpnConnectionName>: <ip>
```

Example:
```
HomeOffice: 10.66.77.5
```

### 1.4 Empty connection list handling

If `loadVpnConnections()` returns an empty list:

- The submenu **must still exist**.
- It must contain a single disabled item:

```
(No VPN connections available)
```

- Tray tooltip must clearly indicate a fatal configuration issue:

```
No VPN connections configured
```

- The tray icon color must be **grey**.

This is a hard failure; the UI must not silently hide the problem.

### 1.5 Selecting a different VPN connection

When the user clicks a VPN connection menu item:

- The selected VPN connection name **must be persisted** using:
  - `vpnConnectionNameKey`
  - `AppSettingsProvider`
- The checked state updates **immediately**.
- The VPN is **not** restarted automatically.
- A visible indication that **reconnection is required** must be shown:
  - Tooltip suffix: `"(reconnect required)"`

Example tooltip:
```
HomeOffice: 10.66.77.5 (reconnect required)
```

- A log entry at `Info` level must be written indicating old → new VPN name.

### 1.6 Changing connection while connected

- Changing VPN connection **while connected is allowed**.
- The running VPN session is **not** modified.
- The UI must clearly communicate that the change will only take effect after disconnect + reconnect.

---

## 2. Auto Start toggle

### 2.1 Menu item

- Add a checkbox menu item named **"Auto Start"**.
- Place it directly **below** the "VPN Connection" submenu and **above** `Exit`.

### 2.2 Initial state

- Initial checked state is loaded via `loadAutoStart()`.

### 2.3 Toggle behavior

On click:

- The value is flipped.
- The new value is persisted using:
  - `autoStartKey`
  - `AppSettingsProvider`
- The checkbox state updates immediately.
- An `Info` log entry is written indicating the new value.

No service restart or VPN restart is triggered.

---

## 3. Asynchronous service status querying

### 3.1 Problem statement (current bug)

- Service status queries are currently executed on the UI thread.
- If the client service is down or blocked, the tray UI becomes unresponsive.

This must be fixed.

### 3.2 Mandatory async model

- **All service status queries must be executed off the UI thread**.
- UI updates must be marshalled back onto the UI thread.
- At most **one active status query** may run at a time.

### 3.3 Affected operations

The following operations must use the async status query path:

- Initial status query during tray startup
- Hover-based refresh (`MouseMove`)
- Post start/stop status refresh

### 3.4 Intermediate UI state

While a background query is in progress:

- Tray UI state must switch to an explicit **query-in-progress** state.
- Tooltip text:

```
Checking service...
```

- Tray icon color: **grey**

This state must be visible immediately when the query starts.

### 3.5 Completion handling

When the background query completes:

- UI state is updated atomically on the UI thread.
- Tray icon and tooltip are refreshed exactly once.
- If the service is unreachable, the final state is `ServiceNotRunning`.

---

## 4. TrayUiState changes

- Extend `TrayUiState` to explicitly represent the **query-in-progress** condition.
- This is a UI-only state; it must not be confused with service-level states.

---

## 5. AppSettings helpers (required)

CC may add **helper functions only** to `AppSettings.fs` to support the UI:

Required helpers:

- Persist selected VPN connection name using `vpnConnectionNameKey`
- Persist Auto Start flag using `autoStartKey`

Rules:

- Use `AppSettingsProvider.tryCreate()`
- Use `provider.trySet`
- Use `provider.trySave()`
- Return `Result<unit, _>` consistent with existing patterns

No direct JSON manipulation is allowed.

---

## 6. Logging requirements

- VPN connection change: `Info`
- Auto Start toggle: `Info`
- Service unreachable / exceptions: existing behavior preserved

---

## 7. Non-goals (explicitly forbidden)

- No automatic reconnect on VPN change
- No UI thread blocking calls
- No direct `appsettings.json` access
- No renaming of existing types, functions, or keys
- No alternative UI designs

---

## End of specification

This document is authoritative. CC must implement exactly what is specified above.

