# vpn_gateway_spec__056__client_device_binding_hash.md

## Importrant
Note that part of F# code related to `VpnClientConfigData` and `VpnClientHash` has been implemented. In case of naming discrepancies the already implemented names must be used.
 


## Purpose

Prevent **two different physical devices** from using the **same client key** against the server (which currently causes session kicking and infinite reconnect storms).

This spec binds each `VpnClientId` (key) to **exactly one device** using a **stable, privacy-safe device hash**.

---

## Scope

This change includes:

- Computing a **stable per-device hash** on **Windows** and **Android**
- Adding `VpnClientHash` to `VpnAuthRequest`
- Server-side **enforcement + first-use binding** using `{clientId.value}.hash` in `data.serverAccessInfo.clientKeysPath`

Not in scope for this spec:

- Stopping client reconnect behavior on auth failure
- Changing `VpnAuthResponse` (explicitly NOT done here)
- Any UI changes

---

## Core Rule (Authoritative)

For each `VpnClientId`:

- The server must accept authentication only if the client presents the **same** `VpnClientHash` that is already bound to that `VpnClientId`.
- If no hash is bound yet, the **first successful auth** binds it permanently by creating `{clientId.value}.hash`.

Re-binding is performed only by **manual deletion** of `{clientId.value}.hash` on the server.

---

## Server Requirements

### Hash file location and name

- Folder: `data.serverAccessInfo.clientKeysPath`
- File name: `FileName $"{clientId.value}.hash"`

### Read / store behavior

Inside `member private r.tryGetClientConfig (clientId : VpnClientId)` in:

- `C:\GitHub\Softellect\Vpn\Server\ClientRegistry.fs`

Behavior:

1. If `{clientId.value}.hash` exists:
   - Read it as **ASCII** string (trim end-of-line)
   - Store it into `VpnClientConfigData` as `VpnClientHash option`
2. If it does not exist:
   - `VpnClientConfigData.clientHash` is `None`

### Enforce on authentication

During authentication handling:

- `VpnAuthRequest` MUST include `VpnClientHash`
- If server has stored hash for the client:
  - If request hash matches stored hash → proceed as today
  - If request hash differs → authentication fails immediately (use existing failure path)
- If server has no stored hash file:
  - Create `{clientId.value}.hash`
  - Write the request hash string to it (ASCII, no BOM, newline allowed but keep read/trim consistent)
  - Proceed as today

**Important:** Do not overwrite an existing `.hash` file.

---

## Client Requirements (Windows + Android)

### What the client must send

`VpnAuthRequest` must carry:

- `VpnClientHash` (the already-defined type you referenced)

The hash is sent on **every** authentication attempt.

### Hash format (strict)

- ASCII-only string
- Deterministic and stable on the same device
- Use `sha256` of a stable device identifier string
- Encode digest as **lowercase hex** (ASCII)

No other encoding is allowed in this spec.

---

## Stable device identifier sources (strict, no alternatives)

### Windows device identifier source

Use the Windows machine GUID:

- Registry key: `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography`
- Value name: `MachineGuid`

Rules:

- Read as string
- Use exactly the raw string value as the input to hashing (do not append extra fields)
- If missing / unreadable → authentication must fail (do not invent a fallback)

### Android device identifier source

Use Android Secure ID:

- `Settings.Secure.ANDROID_ID`

Rules:

- Read as string from the Android system
- Use exactly the raw string value as the input to hashing (do not append extra fields)
- If missing / unreadable → authentication must fail (do not invent a fallback)

---

## Hash computation (strict)

Given `deviceIdString` (from above, platform-specific):

1. Convert to UTF-8 bytes
2. Compute `sha256(bytes)`
3. Convert digest to **lowercase hex** string
4. Wrap into `VpnClientHash`

No salting, no truncation.

---

## Data flow summary (authoritative)

1. Client computes `VpnClientHash` from the platform device id
2. Client includes `VpnClientHash` in every `VpnAuthRequest`
3. Server loads existing `{clientId}.hash` if present
4. If no file exists, server creates it and stores the received hash
5. If file exists, server rejects auth when the received hash differs

---

## Operational procedure (manual re-bind)

To move a key to a new device:

- Server operator manually deletes `{clientId.value}.hash` from `clientKeysPath`
- Next successful auth from the intended device will re-create and bind the file

---

## Contradictions Rule (Important)

If anything in the existing codebase contradicts this spec (for example:
- `VpnAuthRequest` cannot be extended as required,
- platform APIs are not accessible in the current project setup,
- auth failure cannot be expressed using existing paths)

👉 STOP IMMEDIATELY  
👉 ASK FOR CLARIFICATION  
👉 DO NOT IMPLEMENT WORKAROUNDS  
👉 DO NOT ADD FALLBACK IDENTIFIERS

---

**End of specification.**
