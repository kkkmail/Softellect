# vpn_gateway_spec__064__android_vpn_connections_deserialization_and_selection

## Scope

This specification defines **mandatory** changes to the Android VPN client so that it supports **Windows‑style `vpnConnections`**, uses the **same deserialization semantics** as Windows, and allows the user to **select and persist** a VPN connection name.

There are **no options**. Claude Code (CC) must implement exactly what is specified here.

Primary files:
- `C:\GitHub\Softellect\Vpn\AndroidClient\ConfigManager.fs`
- `C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\MainActivity.fs`

Constraints:
- Changes must be **minimal** and localized.
- **No legacy support**: Android must no longer rely on `serverHost`, `basicHttpPort`, or `udpPort`.
- Android JSON uses **camelCase** keys.
- All new F# identifiers must use **camelCase**.
- The config file is bundled in APK Assets and is **read‑only at runtime**.
- Persist user overrides using **SharedPreferences** only.

---

## 1. Authoritative reference: Windows behavior

Before implementing Android changes, CC **must study**:

- `loadVpnConnections` in  
  `C:\GitHub\Softellect\Vpn\Core\AppSettings.fs`

This function defines the **canonical behavior** for:
- Enumerating VPN connection names
- Parsing serialized service info strings of the form  
  `HttpServiceInfo|...`
- Producing typed `VpnConnectionInfo` values

Android must reproduce the **same semantics**, even if the implementation details differ.

---

## 2. Android config format (Assets)

The Android app must use `vpn_config.json` in Assets with the following required keys:

### Required keys (camelCase)

- `vpnConnectionName` : string  
  Default VPN connection name.

- `vpnConnections` : JSON object  
  Mapping from connection name → serialized service info string.

### Placement rule

- `vpnConnections` **must be the last property** in the JSON object.

### Example shape (illustrative only)

```json
{
  "vpnConnectionName": "VPN Connection 1",
  "clientId": "...",
  "serverId": "...",
  "clientPrivateKey": "...",
  "clientPublicKey": "...",
  "serverPublicKey": "...",
  "useEncryption": true,
  "encryptionType": "AES",
  "vpnConnections": {
    "VPN Connection 1": "HttpServiceInfo|httpServiceAddress=127.0.0.1;httpServicePort=45001;httpServiceName=VpnService",
    "VPN Connection 2": "HttpServiceInfo|httpServiceAddress=127.0.0.1;httpServicePort=45001;httpServiceName=VpnService"
  }
}
```

No other connection‑related fields are allowed.

---

## 3. ConfigManager.fs changes

### 3.1 VpnClientConfig type

`VpnClientConfig` must:
- Include `vpnConnectionName : string`
- Include list of `vpnConnections`, similar to `vpnConnections` from `type VpnClientAccessInfo` from `C:\GitHub\Softellect\Vpn\Core\ServiceInfo.fs`
- Remove all legacy fields related to host/ports

CC is free to deserialize `vpnConnections` using:
- `JObject`, or
- a temporary dictionary/map

**Important:** this raw representation must be treated as **transient**.

---

## 3.2 Deserialization rule (critical)

CC **must not invent a new parser** for service info strings.

Instead:
- Inspect what `loadVpnConnections` does internally
- Identify how it parses `"HttpServiceInfo|..."` strings
- Use the **same parsing utilities** from shared Core code (`ServiceInfo`, `Wcf.Common`, etc.)

Android deserialization must:
1. Enumerate connection names from `vpnConnections`
2. For each entry:
   - Parse the serialized string using the same logic Windows uses
   - Build `VpnConnectionInfo` values
3. Produce a list of valid VPN connections

### Error handling rules

- If `vpnConnections` is missing or empty → return `Error "No VPN connections configured"`
- If parsing fails for the **selected** connection → return `Error`
- If parsing fails for **non‑selected** connections:
  - Log a warning
  - Skip them
  - Continue only if at least one valid connection remains

---

## 3.3 Effective VPN connection name resolution

Add logic in `ConfigManager` to resolve the effective VPN connection name:

Order:
1. Persisted value from SharedPreferences (if present)
2. `vpnConnectionName` from config

Validation:
- If the resolved name is not present among parsed connections:
  - Log a warning
  - Fall back to the **first available** connection

---

## 3.4 SharedPreferences persistence

Use SharedPreferences for persistence.

Constants:
- Preferences name: `SoftellectVpn`
- Key: `vpnConnectionName`

Required helpers:
- Load persisted name → `string option`
- Persist selected name → `unit`

Use `Apply()` (non‑blocking).

---

## 3.5 toVpnClientServiceData

`toVpnClientServiceData` must:
- Use the **resolved** VPN connection name
- Use the parsed connection corresponding to that name
- Populate:
  - `vpnConnectionInfo.vpnConnectionName`
  - `vpnConnectionInfo.serverAccessInfo`
  - `vpnConnections` collection (type is free: list or map)

Remove the deliberate non‑compiling placeholders:
- `vpnConnectionName = 0`
- `vpnConnections = 0`

---

## 4. MainActivity.fs changes (UI)

### 4.1 VPN connection selector

Add a **Spinner** control.

Placement:
- In the existing top control row
- To the **right of** the status text

Behavior:
- Items: VPN connection names
- Initial selection: resolved effective name

---

### 4.2 Selection change behavior

When the user selects a different VPN connection:
- Persist it via SharedPreferences
- Update in‑memory selection
- Log `Info`: old → new
- If VPN is not `Disconnected`, mark **reconnect required**

No automatic reconnect.

---

### 4.3 Reconnect‑required indication

Do **not** add new UI bands.

Requirement:
- Add a boolean `reconnectRequired`
- Display it in the **Info pane** under Configuration:

```
Reconnect required: true|false
```

---

### 4.4 Fatal config handling

If no valid VPN connections exist:
- Set `configLoadError`
- Disable connect button
- Status text: `Config Error`
- Info pane must include the specific error message

---

## 5. Logging requirements

- Invalid persisted name → `Warn`
- User selection change → `Info`
- Parsing failures → `Warn` with connection name

---

## 6. Files allowed to change

Only:
- `ConfigManager.fs`
- `MainActivity.fs`

Unless a compiler error requires a **directly related** fix.

---

## End of specification

This document is authoritative. CC must implement exactly what is specified above.
