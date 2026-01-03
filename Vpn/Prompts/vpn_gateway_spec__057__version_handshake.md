# vpn_gateway_spec__057__version_handshake.md

## Goal

Add a **version handshake** between client and server, using a new “cut-in-stone” WCF method that returns:

- `serverBuildNumber` (the server’s current build number)
- `minAllowedClientBuildNumber` (the minimum client build number the server will accept)

The version handshake must be performed **before** authentication.

Notes / constraints:

- This codebase uses CoreWCF as a **byte[] transport** and then performs its own serialization/deserialization of F# records/unions.
- Therefore, existing serialized F# structures **cannot be “extended”** (deserialization would fail).
- This new method and its response record are intended to remain **stable** over time.

NNN = 057.

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
- `IAuthWcfService`

Signature for `IAuthClient`:

- `unit -> VpnVersionInfoResponse`

Method name:

- `getVersionInfo`

(Use the existing naming conventions for interface members where required, but keep the new method name itself simple and stable.)

### Server behavior for getVersionInfo

Implement `getVersionInfo` so it returns:

- `serverBuildNumber` = `BuildInfo.BuildNumber` on the server
- `minAllowedClientBuildNumber` = a server-controlled value, described below

### Where minAllowedClientBuildNumber comes from

Add a hard coded server value (40)

## Client gating logic (shared idea)

Before attempting authentication:

1. Call `getVersionInfo`.
2. Let:
   - `clientBuild` = local `BuildInfo.BuildNumber`
   - `serverBuild` = response.serverBuildNumber
   - `minAllowed` = response.minAllowedClientBuildNumber

3. Decide:

- If `clientBuild < minAllowed`:
  - **fail fast** (do not call auth)
  - surface an error message that includes: clientBuild, serverBuild, minAllowed
- Else if `clientBuild < serverBuild`:
  - “obsolete but supported”
  - proceed to auth
  - surface a warning status (UI/log)
- Else:
  - proceed to auth normally

### Transient failures during version check

If `getVersionInfo` fails due to a transient communication issue (timeout, connection failure, etc.):

- Apply the same retry policy you already have for auth transient failures (i.e., retry version check before auth, rather than going straight to auth).

## Windows client behavior

The Windows client can run as:

- Windows service
- Console/EXE

Implementation requirements:

1. Integrate the version check **before** auth in the connect flow.
2. If version is **unsupported** (`clientBuild < minAllowed`):
   - Do not attempt auth.
   - Do not enter the auth retry loop.
   - Return/raise an error that clearly communicates “client too old”.
   - Ensure that any “VPN enablement” actions that happen only after auth remain unchanged.
     - Important: do not leave networking in a half-enabled state due to this new failure mode.

3. If version is “obsolete but supported”:
   - Proceed to auth (and auth retries remain as today).
   - Emit a warning in logs/status that includes clientBuild and serverBuild.

4. If version is current (`clientBuild >= serverBuild`):
   - Proceed as today.

## Android client behavior (UI-specific)

Android flow:

- When the user clicks **Connect**:
  1) call `getVersionInfo`
  2) apply gating logic
  3) if allowed, proceed to authenticate

UI requirements:

1. If `clientBuild < serverBuild` but `clientBuild >= minAllowed`:
   - Update the top bar where it says “Softellect VPN” to be **more yellowish** (visual “obsolete but supported”).
   - Proceed to auth.

2. If `clientBuild < minAllowed`:
   - Update that top bar to be **more reddish** (visual “unsupported”).
   - Do NOT attempt auth.
   - Add the error to the info panel (this should happen automatically if you propagate the error through the existing error pipeline).

3. Info panel must include:
   - `clientBuild`
   - `serverBuild`
   - `minAllowedClientBuildNumber`

(Exact formatting is flexible; keep it simple and readable.)

## Error message / status strings

Add a distinct error message for unsupported client versions, including:

- clientBuild
- serverBuild
- minAllowed

Example text (exact text can differ, but must include the numbers):

- “Client build {clientBuild} is below minimum supported {minAllowed} (server build {serverBuild}). Upgrade required.”

For “obsolete but supported”, add a warning string:

- “Client build {clientBuild} is older than server build {serverBuild}, but is still supported (min {minAllowed}).”

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
   - read config `minAllowedClientBuildNumber` (default / clamp rules above)

4. Windows client connect/auth flow
   - call version check before auth
   - apply retry-on-transient-failure during version check
   - fail-fast on unsupported (no auth retries)

5. Android client connect flow + UI updates
   - call version check before auth
   - set UI “yellowish” / “reddish” states
   - add version info to info panel

## Implementation notes / guardrails

- Keep this change minimal and surgical.
- Do not rename existing types/members even if they are ugly; only ensure **new** names follow camelCase conventions.
- Do not introduce any new serialization mechanism; use the same serialization approach already used for auth messages.
- The new response record must be serialized/deserialized in the same way as other primitives, but it must be treated as stable going forward.

## Quick test checklist

1. Current client + current server:
   - version check passes
   - auth proceeds normally

2. Client build < server build but >= minAllowed:
   - Android: yellowish header
   - Windows: warning log/status
   - auth proceeds

3. Client build < minAllowed:
   - Android: reddish header + info panel error; no auth call
   - Windows: connect fails fast; no auth retries

4. Transient server unreachable:
   - version check retries (same policy as auth retries)
   - final failure behaves like existing “cannot connect” behavior
