# VPN Android Client — Detect Other VPN + Foreground Service Resilience (Spec 065)

Target: `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\MainActivity.fs`

This spec defines two fixes:

1. **Other VPN detection**: if any other VPN is active, the app must **inform the user in the existing Info/Log panels and do nothing else**.
2. **Long-idle resilience**: make the VPN connection survive long idle periods by running the VPN implementation as a **foreground service**, without requiring root.

This is an authoritative Claude Code (CC) implementation plan. No optional paths.

---

## 0. Non-goals

- No rooting, no device-owner / MDM requirements.
- No “best effort” fallback that tries to continue when another VPN is active.
- No “advanced” UX. Just a clear message in Info/Log and an early return.
- No new UI panels. Use the existing Info + Log panels only.

---

## 1. Fix #1 — Detect another VPN and stop

### 1.1 Requirement

On app start and on app resume:

- If there is an **active VPN on the device** and it is **not our own active tunnel**, then:
  - Append a clear message to the **Info panel** and to the **Log panel**.
  - Disable the connect action (or keep it enabled but have it no-op with the same message).
  - Do **not** load config.
  - Do **not** attempt any network state reads that can fail due to permissions.
  - Do **not** call into VPN connection code.
  - Return early from the flow.

Message text must be short and unambiguous, for example:

- Info: `Another VPN is currently active on this device. Disconnect it first, then reopen this app.`
- Log:  `Detected active VPN transport: blocking actions until the other VPN is disconnected.`

### 1.2 Definition: “not our own active tunnel”

Because Android allows only one VPN at a time, “our own” means:

- Our VPN foreground service is running **and**
- Our VPN service reports its tunnel state as Connected (or equivalent) **and**
- The app considers itself connected to the tunnel.

If the device reports an active VPN transport but our service is not in Connected state, treat it as “other VPN”.

### 1.3 Implementation rule

Implement a single function that answers:

- `isSomeVpnActiveOnDevice : bool`

It must use only system connectivity state (ConnectivityManager + NetworkCapabilities check for VPN transport). It must not depend on config, not depend on UI state, and not call any other subsystem.

Then implement:

- `isOtherVpnBlocking : bool = isSomeVpnActiveOnDevice && not isOurVpnConnected`

### 1.4 Where to run the check

Run the “other VPN” check at the earliest safe points:

- `OnCreate` before any config loading or other initialization that can fail.
- `OnResume` (or equivalent lifecycle callback) before enabling connect/disconnect actions.

If blocking is detected on resume, the UI must remain responsive and simply show the message.

### 1.5 Logging

Add one log line whenever blocking is detected, and one log line when it is cleared (transition from blocked -> not blocked).

No spam. Only on state change.

---

## 2. Fix #2 — Foreground service for long-idle stability

### 2.1 Requirement

When connected, the VPN must run in a dedicated service that is:

- A `VpnService`-based implementation (or your existing VpnService-derived type).
- Promoted to a **foreground service** for the entire connected duration.
- Uses an ongoing notification (persistent) while connected.
- Can be restarted by Android if the process is reclaimed (service is sticky).

This must not require root.

### 2.2 Expected behavioral change

After implementing the foreground service:

- Leaving the VPN connected for hours must not result in a wedged state where:
  - The status indicator suggests VPN is present but traffic is not flowing, and
  - Opening the app becomes unresponsive, and
  - Only a device reboot resolves the issue.

If Android or OEM firmware still kills the process, the design must recover by either:
- Automatically restarting and reconnecting the tunnel (if configured to do so), or
- Cleanly reporting Disconnected state and allowing the user to reconnect.

No UI freeze is acceptable.

### 2.3 Service responsibilities (single source of truth)

The service owns:

- Tunnel lifecycle: start, stop, reconnect.
- Current tunnel state (Disconnected / Connecting / Connected / Error).
- Health monitoring (see 2.6).
- Notification management.

The Activity (MainActivity) owns:

- Display (Info/Log panels).
- User actions (Connect/Disconnect button).
- Binding to the service to query state.
- Never blocking the UI thread on service calls.

### 2.4 Foreground notification

Implement:

- Notification channel creation (once).
- An ongoing notification shown while Connected and Connecting.
- `startForeground(...)` must happen immediately when starting the connection flow and remain active until fully stopped.

Use a minimal notification:
- Title: `Softellect VPN`
- Text: `Connected` / `Connecting...` / `Reconnecting...`
- Tap action: opens the app.

### 2.5 Start/stop rules

- Connect button:
  - If “other VPN blocking” is true: no-op with message (Fix #1).
  - Else: request service to connect.
- Disconnect button:
  - Requests service to stop tunnel.
  - Service removes foreground notification.
  - Service state becomes Disconnected.

Service should be `START_STICKY` (or the closest Android-equivalent in your binding model) so it can be restarted.

### 2.6 Health monitoring (minimal, mandatory)

Implement a minimal watchdog inside the service:

- Track last “progress/traffic” timestamp (define what your VPN considers activity: received packets, heartbeat reply, etc.).
- If Connected but no activity for a long threshold (e.g., minutes), transition to Reconnecting:
  - Stop tunnel cleanly.
  - Start tunnel again.
  - Update notification text.

If reconnect fails N times, transition to Error and require user action.

This must be logged with timestamps.

### 2.7 Lifecycle handling

Handle these callbacks in a controlled way:

- Service `onRevoke`: treat as forced disconnect, update state, drop notification, log.
- Service `onDestroy`: log, transition state, cleanup.
- Network changes:
  - When the underlying network changes, do not freeze. Either reconnect or keep alive depending on your protocol, but always keep the UI responsive.

### 2.8 Battery optimization note (non-blocking)

Do not require the user to change battery settings to function.

However, add a one-time Info panel hint (only once per install, persisted) if you detect aggressive background killing symptoms:

- `If you experience disconnects after long idle, exclude Softellect VPN from battery optimizations in Android settings.`

No deep-linking is required.

---

## 3. MainActivity integration points

### 3.1 Early “other VPN” gate

In `MainActivity.fs`:

- Insert an early check in OnCreate.
- If blocked: write Info/Log message and return from the initialization path (do not call config load, do not wire up connect action beyond a no-op message).

### 3.2 Service binding and state refresh

On resume:

- Bind (or re-bind) to the VPN foreground service.
- Query current tunnel state from service.
- Update Info panel:
  - `Connected` / `Disconnected` / `Connecting` / `Error: ...`
- If blocked by other VPN: show the blocking message.

All of this must happen without blocking the UI thread.

### 3.3 “UI unresponsive” hard rule

Any operation that can block (IPC, network, waiting for service) must be dispatched off the UI thread. The Activity must remain responsive even if the service is dead or restarting.

---

## 4. Acceptance tests (manual)

### 4.1 Other VPN detection

1. Connect to a different VPN app.
2. Open Softellect VPN Android client.
3. Expected:
   - Info/Log shows the “another VPN active” message.
   - Connect does nothing beyond repeating the message.
   - App does not crash.
4. Disconnect the other VPN.
5. Resume the app.
6. Expected:
   - Blocking message clears (log only on transition).
   - Connect becomes functional.

### 4.2 Long-idle stability

1. Connect Softellect VPN.
2. Leave phone idle for several hours (screen off).
3. Open the app.
4. Expected:
   - UI remains responsive.
   - App state reflects real tunnel state.
   - If the tunnel was killed, it is either:
     - auto-reconnected, or
     - cleanly shown as disconnected/error with ability to reconnect.
   - No reboot is needed to recover.

---

## 5. Deliverables

CC must implement:

- Other VPN early gate (Fix #1).
- Foreground service migration/enablement for VPN runtime (Fix #2).
- Service state API for Activity to query state safely.
- Minimal health watchdog and logging.

No unrelated refactors.
