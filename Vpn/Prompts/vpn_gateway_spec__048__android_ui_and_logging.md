# vpn_gateway_spec__048__android_ui_and_logging.md

## Purpose

Define **mandatory UI, logging, and diagnostics behavior** for the Android VPN client.

This spec is **authoritative**.  
It replaces any previous vague or conceptual UI/logging guidance.

Scope:
- Android UI layout
- Status & controls
- Config visibility
- Runtime stats
- Logging capture and display
- Error and permission visibility

No protocol changes are introduced.

---

## Starting point (mandatory)

All work starts from:

```
C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\
```

CC must follow the project structure and conventions found there and extend them as needed.

---

## 1. Config handling (frozen)

- **NO “IMPORT CONFIG” button**
- Config is **automatically loaded from Assets** at app startup
- Failure to load config:
  - VPN **cannot start**
  - Error must be shown in **Info pane (Last error)**
  - Full error must appear in **Log pane**

---

## 2. Top control row (mandatory layout)

### Layout
Single horizontal row at the top of the screen.

**Left**
- Round **power button**

**Right**
- Connection status text

They must be on the **same line**.

### Power button
- Shape: **round**
- Icon: **standard power symbol**
- States:
  - Disconnected → **pale red**
  - Connecting → **pale yellow / amber**
  - Connected → **pale green**
- Colors must be **muted / pastel**, not saturated
- Size: ~48–56dp diameter
- Connecting state may use a **subtle pulse or rotation**

### Status text
- Text: `Disconnected`, `Connecting…`, `Connected`
- Color matches button state (slightly darker)
- No extra banners, no popups

---

## 3. Main content layout (mandatory)

Screen is split into **two panes**:

1. **Info pane** (static + critical)
2. **Log pane** (dynamic logs)

### Orientation behavior
- **Portrait** → panes stacked vertically
- **Landscape** → panes side by side

---

## 4. Info pane (mandatory content)

Scrollable text area.

### Must include

**Configuration**
- Server host
- BasicHttp port
- UDP port
- ClientId
- ServerId

**Session**
- SessionId (when connected)

**Traffic**
- Bytes sent
- Bytes received

**Network (MANDATORY)**
- Current network type:
  - Wi‑Fi / Cellular / Unknown
- Physical interface name
- Gateway IP

**Errors (MANDATORY)**
- **Last error** (single line)
- Permission violations MUST appear here

### Copy support
- Small icon-only copy button (📋)
- Copies entire Info pane text to clipboard

---

## 5. Log pane (mandatory behavior)

Scrollable, monospaced text.

### Content
- All runtime logs
- Timestamped
- Append-only

### Behavior
- Auto-scroll when at bottom
- If user scrolls up, auto-scroll pauses
- No pinned or static messages at top

### Copy support
- Small icon-only copy button (📋)
- Copies entire log buffer to clipboard

---

## 6. Logging capture (MANDATORY IMPLEMENTATION)

This is **not conceptual**.

CC must implement a logging sink that:
- Writes logs to:
  - normal logging backend (if any)
  - **in-memory log buffer**
- Log pane reads from this buffer

### Requirements
- Thread-safe
- Bounded size (ring buffer)
- Oldest entries dropped when limit reached
- UI observes / refreshes from buffer

Permission errors, exceptions, and failures MUST be logged.

---

## 7. Permissions & errors (mandatory visibility)

- VPN permission denial
- Network permission issues
- Socket binding failures
- WCF/auth failures
- UDP send/receive failures

All must:
- Appear in **Log pane**
- Latest one reflected in **Info pane → Last error**

No silent failures.

---

## 8. Interaction rules

Power button behavior:
- Disconnected → tap → Connecting
- Connecting → tap → Cancel → Disconnected
- Connected → tap → Disconnect

Button disabled **only** on fatal config load failure.

---

## 9. Visual style (constraints)

- Engineering / diagnostics oriented
- No glossy UI
- No animations except subtle state feedback
- High contrast text for logs
- Light or dark theme is acceptable but must be consistent

---

## 10. Non-goals (explicit)

- No Play Store polish
- No onboarding
- No settings editor
- No key import/export UI
- No protocol changes

---

## Acceptance criteria

- App starts and loads config automatically
- UI matches layout rules above
- Network type, interface, and gateway are visible
- Logs are visible in real time
- Copy buttons work for both panes
- Permission and runtime errors are never silent

---
End of spec 048.
