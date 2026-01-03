# vpn_gateway_spec__057__version_handshake__V02.md

## Required
In case of discrepancies, stop and ask.

## Goal

Add a **version handshake** between client and server, using a new “cut-in-stone” WCF method that returns:

- `serverBuildNumber` (the server’s current build number)
- `minAllowedClientBuildNumber` (the minimum client build number the server will accept)

The version handshake must be performed **before** authentication.

Notes / constraints:

- This codebase uses CoreWCF as a **byte[] transport** and then performs its own serialization/deserialization of F# records/unions.
- Therefore, existing serialized F# structures **cannot be “extended”** (deserialization would fail).
- This new method and its response record are intended to remain **stable** over time. If you ever need to evolve it, add a *new* method instead of changing this one.

## Source of truth for build number

The build number is an `int` value:

- `C:\GitHub\Softellect\Sys\BuildInfo.fs`
- value name: `BuildNumber`

Both client and server must read their own local `BuildNumber` from that module.

## New response record

Add a new record type to:

- `C:\GitHub\Softellect\Vpn\Core\Primitives.fs`

Record name (new names in this change should be camelCase F# style; do not rename any existing “already fucked up” names):

- `VpnVersionInfoResponse`

Fields (ints):

- `serverBuildNumber`
- `minAllowedClientBuildNumber`

This record is part of the **stable handshake** contract.

## New WCF method (stable handshake)

Add a new method to both the client-side and server-side auth WCF interfaces:

- `IAuthClient`
- `IAuthService`
- `IAuthWcfService`

Signature (for `IAuthClient` and `IAuthService`):

- `unit -> VpnVersionInfoResponse`

Method name:

- `getVersionInfo`

(Use the existing naming conventions for interface members where required, but keep the new method name itself simple and stable.)

## Server behavior for getVersionInfo

Implement `getVersionInfo` so it returns:

- `serverBuildNumber` = `BuildInfo.BuildNumber` on the server
- `minAllowedClientBuildNumber` = a **hard-coded server constant** (see below)

### Hard-coded minimum supported client build (server side)

Add a hard-coded server constant:

- `minAllowedClientBuildNumber = 40`

No config plumbing and no rechecks.

## Client-side minimum supported server build (client side)

There is a missing symmetric case: **clientBuild > serverBuild** means the server could be too obsolete for the client.

Add a **hard-coded client constant**:

- `minAllowedServerBuildNumber = 40`

This is the minimum server build the client is willing to work with.

Important:

- This constant is local to the client (Windows + Android).
- It must be treated as “cut in stone” in the same way as the server’s hard-coded minAllowedClientBuildNumber, but lives on the client side.
- If the project later needs more sophisticated negotiation, add a *new* method; do not mutate this handshake.
- The constant must be placed into C:\GitHub\Softellect\Vpn\Client\WcfClient.fs to be shared between Android and Windows code.

## Version gating logic (shared idea)

Before attempting authentication:

1. Call `getVersionInfo`.
2. Let:
   - `clientBuild` = local `BuildInfo.BuildNumber`
   - `serverBuild` = response.serverBuildNumber
   - `minAllowedClientByServer` = response.minAllowedClientBuildNumber
   - `minAllowedServerByClient` = the hard-coded client constant (above)

3. Decide compatibility in both directions:

### ERROR (unsupported) conditions (fail fast, do not call auth)

Fail fast if **either** is true:

- Client too old for server:
  - `clientBuild < minAllowedClientByServer`
- Server too old for client:
  - `serverBuild < minAllowedServerByClient`

In ERROR:

- Do **not** call auth.
- Surface an error message that includes: clientBuild, serverBuild, minAllowedClientByServer, minAllowedServerByClient.

### WARN (obsolete but supported) conditions (proceed to auth)

If not ERROR, but there is a mismatch in either direction:

- Client is behind server:
  - `clientBuild < serverBuild`
- Server is behind client:
  - `serverBuild < clientBuild`

In WARN:

- Proceed to auth.
- Surface a warning status (UI/log) that includes the same version details.

### OK (match)

If not ERROR and not WARN:

- Proceed to auth normally (versions match, i.e. `clientBuild = serverBuild`).

### Transient failures during version check

If `getVersionInfo` fails due to a transient communication issue (timeout, connection failure, etc.):

- Apply the same retry policy you already have for auth transient failures (i.e., retry version check before auth, rather than going straight to auth).
- No background rechecks.

If retries are exhausted, treat it as a connect failure (as today).

## Windows client behavior

The Windows client can run as:

- Windows service
- Console/EXE

Implementation requirements:

1. Integrate the version check **before** auth in the connect flow.
2. If version is **ERROR** (unsupported in either direction):
   - Do not attempt auth.
   - Do not enter the auth retry loop.
   - Return/raise an error that clearly communicates the reason (client too old **or** server too old).
   - Ensure that any “VPN enablement” actions that happen only after auth remain unchanged.
     - Important: do not leave networking in a half-enabled state due to this new failure mode.

3. If version is **WARN**:
   - Proceed to auth (and auth retries remain as today).
   - Emit a warning in logs/status that includes the version details.

4. If version is **OK**:
   - Proceed as today.

## Android client behavior (UI-specific)

Android flow:

- When the user clicks **Connect**:
  1) call `getVersionInfo`
  2) apply gating logic (OK / WARN / ERROR)
  3) if allowed, proceed to authenticate

UI requirements (apply regardless of which side is obsolete):

1. WARN (obsolete but supported; either client behind server or server behind client):
   - Update the top bar where it says “Softellect VPN” to be **more yellowish**.
   - Proceed to auth.

2. ERROR (unsupported; either client too old for server or server too old for client):
   - Update that top bar to be **more reddish**.
   - Do NOT attempt auth.
   - Add the error to the info panel (this should happen automatically if you propagate the error through the existing error pipeline).

3. Info panel must include:
   - `clientBuild`
   - `serverBuild`
   - `minAllowedClientBuildNumber` (from server)
   - `minAllowedServerBuildNumber` (hard-coded client constant)

(Exact formatting is flexible; keep it simple and readable.)

## Error message / status strings

Add a distinct error message for the ERROR cases, including all values:

- clientBuild
- serverBuild
- minAllowedClientByServer
- minAllowedServerByClient

Example texts (exact text can differ, but must include the numbers):

- Client too old:
  - “Client build {clientBuild} is below minimum supported {minAllowedClientByServer} (server build {serverBuild}). Upgrade client required.”
- Server too old:
  - “Server build {serverBuild} is below minimum supported {minAllowedServerByClient} (client build {clientBuild}). Upgrade server required.”

For WARN, add a warning string:

- “Version mismatch: client {clientBuild}, server {serverBuild}; supported (server min client {minAllowedClientByServer}, client min server {minAllowedServerByClient}).”

## Files to change (expected)

At minimum, expect edits in these areas:

1. `C:\GitHub\Softellect\Vpn\Core\Primitives.fs`
   - add `VpnVersionInfoResponse`

2. WCF contract files defining:
   - `IAuthClient`
   - `IAuthWcfService`
   - add `getVersionInfo : unit -> VpnVersionInfoResponse`

3. Server auth service implementation
   - implement `getVersionInfo`
   - hard-code `minAllowedClientBuildNumber`

4. Windows client connect/auth flow
   - call version check before auth
   - apply retry-on-transient-failure during version check
   - fail-fast on ERROR (no auth retries)
   - WARN logs/status on mismatch

5. Android client connect flow + UI updates
   - call version check before auth
   - set UI “yellowish” for WARN, “reddish” for ERROR
   - add version info to info panel

## Implementation notes / guardrails

- Keep this change minimal and surgical.
- Do not rename existing types/members even if they are ugly; only ensure **new** names follow camelCase conventions.
- Do not introduce any new serialization mechanism; use the same serialization approach already used for auth messages.
- The new response record must be serialized/deserialized in the same way as other primitives, but it must be treated as stable going forward.

## Quick test checklist

1. Current client + current server (same build):
   - version check OK
   - auth proceeds normally

2. Client build < server build, and clientBuild >= minAllowedClientByServer:
   - WARN
   - Android: yellowish header
   - Windows: warning log/status
   - auth proceeds

3. Client build < minAllowedClientByServer:
   - ERROR
   - Android: reddish header + info panel error; no auth call
   - Windows: connect fails fast; no auth retries

4. Client build > server build, and serverBuild >= minAllowedServerByClient:
   - WARN
   - Android: yellowish header
   - Windows: warning log/status
   - auth proceeds

5. Server build < minAllowedServerByClient:
   - ERROR
   - Android: reddish header + info panel error; no auth call
   - Windows: connect fails fast; no auth retries

6. Transient server unreachable:
   - version check retries (same policy as auth retries)
   - final failure behaves like existing “cannot connect” behavior

## Starting Points
Use C:\GitHub\Softellect\Apps\Vpn\VpnAndroidClient\MainActivity.fs as a starting point for Android client.
Use C:\GitHub\Softellect\Vpn\Client\Service.fs as a starting point for Windows client.

## Required
In case of discrepancies, stop and ask.
