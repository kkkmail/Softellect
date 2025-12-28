# vpn_gateway_spec__045__android_auth_ping_and_apk_discovery.md

## Purpose

Stage 1 “dig + report only” task for Claude Code (CC).  
CC must **inspect the existing VPN codebase** and produce a **report** that advises how to add:

1) A **separate Android-friendly auth / ping path** (TCP-based) from Android app → Windows server.  
2) An **Android client APK** that uses **VpnService + TUN** and reuses the existing **UDP data plane**.

This is a “friends & family” VPN. Commercial polish is not required, but the approach must be realistic and maintainable.

**Hard constraint:**  
- **No code changes** in this stage. **Report only.**  
- **No speculative refactors**. Keep recommendations grounded in what is already there.  
- The **data plane is UDP only**. There is **NO TCP data plane**.  
- Current Windows authentication uses **CoreWCF**; for Android we will add a separate TCP auth/ping path (configurable port, possibly “current TCP port + 1”).

## Code locations to inspect (Windows machine)

- `C:\GitHub\Softellect\Vpn\`
- `C:\GitHub\Softellect\Apps\Vpn\`

CC should examine **all projects** under both folders.

## Required deliverable

Create a markdown report file named exactly:

- `C:\GitHub\Softellect\Vpn\Prompts\vpn_gateway_spec__045__report__01.md`

The report must be written as if it will be used to plan implementation immediately after.

## What CC must find and summarize

### 1) Current architecture (as implemented)
Identify (with file paths and project names):
- Where the **Windows server** is implemented:
  - CoreWCF service(s) used for auth (interfaces, operations, bindings, endpoints).
  - Where appsettings / configuration is loaded and validated.
- Where the **Windows client** is implemented:
  - The auth/ping flow today (CoreWCF calls, session establishment, tokens/keys).
  - The UDP tunnel/data-plane sender/receiver code (packet framing, crypto, batching, session IDs, keepalive behavior).
- Where packet parsing/routing/NAT lives:
  - NAT tables, connection tracking keys, mapping logic, timeouts.
  - DNS proxy (if present) and where it hooks into routing.
- Any existing “physical network detection” / interface selection logic and how it feeds into the gateway.

For each item above, include:
- **Project name**
- **Key files**
- **Key types / functions** (names only)
- **Data flow** diagram (simple ASCII) showing auth vs UDP plane.

### 2) What can be reused for Android vs what must change
Given the current code, explicitly classify modules/components into:
- **Reusable as-is**
- **Reusable with minor adaptation** (explain what changes)
- **Not reusable** (explain why)

Keep this focused on:
- UDP framing/crypto
- session identifiers / handshakes
- packet router expectations (e.g., does server rely on stable client UDP source port, or payload session id?)

### 3) Android requirements and how they map to current design
CC must propose an Android-side design that fits Android constraints:
- Use **Android VpnService** (TUN interface) to read/write raw IP packets in userspace.
- Handle **network switching** (Wi‑Fi ↔ LTE/5G) via Android connectivity callbacks; explain how the tunnel reconnects/rebinds.
- Ensure tunnel sockets bypass the VPN (i.e., protect/bind so packets don’t loop back into TUN).

The report should explicitly answer:
- Where does the “packet pump” live (service, thread model)?
- How does it connect to server (UDP socket lifecycle, reconnect rules)?
- How do we do keepalive and NAT-timeout mitigation?
- What MTU strategy is recommended for mobile networks?

### 4) New Android auth / ping path (server-side)
We will add a **separate TCP auth/ping endpoint** (configurable in appsettings; “existing TCP port + 1” is an acceptable default).

CC must propose:
- Which project(s) should host this endpoint (new project vs existing service project), and why.
- Which protocol style is simplest:
  - Minimal custom TCP framing (length-prefixed messages), OR
  - HTTP/HTTPS REST minimal endpoint, OR
  - gRPC
**Important:** keep recommendations aligned with “friends & family” and minimal dependencies.

The report must include:
- Message sequence for Android auth/ping:
  - authenticate → receive session material → start UDP plane
  - ping/keepalive behavior and how it ties to UDP session liveness
- How this endpoint integrates with existing CoreWCF auth (if at all):
  - e.g., share same auth backend logic, same key store, same client registry.
- Any server firewall / port exposure changes required.

### 5) APK build plan (Android client project)
CC must propose:
- The minimum project structure to build an APK:
  - Kotlin + Android SDK, OR .NET Android (MAUI/Xamarin), OR other.
- Strongly prefer practicality: smallest “path to working APK”.
- How configuration is injected (manual initial setup):
  - server IP, TCP auth port, UDP port, optional DNS settings, etc.
- How logs are collected (for debugging) without heavy tooling.

### 6) Keys and configuration provisioning (manual)
User will provision keys “out of band.” CC must propose **where keys should appear** and how the app reads them.

Provide 2 levels:
- **Simple**: keys as a file in app-private storage (import step, or copy via ADB / file picker / internal screen).
- **Safer**: store long-term secret in Android Keystore; keep config blob encrypted at rest.

Also cover:
- How server access configuration is stored/edited on Android (simple UI screen / config file).
- How to rotate keys (manual process ok).

## Output format requirements for the report

The report `C:\GitHub\Softellect\Vpn\Prompts\vpn_gateway_spec__045__report__01.md` must contain these sections (use these headers):

1. `## Inventory: projects and key files`
2. `## Current flows: Windows auth/ping and UDP data plane`
3. `## Reuse map: what carries over to Android`
4. `## Proposed Android design (VpnService + UDP plane)`
5. `## Proposed Android auth/ping path (server-side TCP endpoint)`
6. `## Manual provisioning: keys and server config`
7. `## Risks / unknowns discovered in code`
8. `## Recommended next step (Stage 2 implementation plan)`

In every section, reference exact file paths and type/function names where relevant.

## Non-goals for Stage 1

- Do **not** write or modify code.
- Do **not** add new projects yet.
- Do **not** “clean up” architecture.
- Do **not** redesign the UDP data plane unless the current code makes it impossible for Android; if you think so, explain precisely why and what minimal change is required.

---

End of spec.
