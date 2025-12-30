# vpn_gateway_spec__051__vpn_apk_server.md

## Goal

Create a **local APK distribution web server** for Android devices, implemented in **F#**, using **Kestrel**, runnable directly as an **EXE**, with **directory browsing enabled** so devices can navigate folders and download APKs.

This server is intended for **local LAN use only** (same router / same room) and replaces any cable-based APK installation workflow.

---

## Project identity

- **Project name:** `VpnApkServer`
- **Location:**  
  `C:\GitHub\Softellect\Apps\Vpn\VpnApkServer\`
- **Runtime:** Windows, x64 only
- **Execution:** run `VpnApkServer.exe` directly (no `dotnet run`)
- **Language:** F#
- **Web server:** ASP.NET Core + Kestrel
- **No IIS. Ever.**

---

## Non‑goals (explicit)

- No Windows service installation (EXE only for now)
- No HTTPS
- No authentication
- No versioning of APKs
- No custom HTML UI
- No code reuse via copy/paste from Windows client logic
- No manual reading of `appsettings.json`

---

## Folder / content model

### Web root

- Static files are served from:
  ```
  <project_root>\wwwroot
  ```

### APK layout (strict)

```
wwwroot  TEST    <standard_apk_name>.apk
  ALICE    <standard_apk_name>.apk
  BOB_01    <standard_apk_name>.apk
```

Notes:
- APK filenames are **identical across users**
- Folder names (`<user_name>`) disambiguate
- CC must not rename APKs
- CC must not manage or populate `wwwroot` contents

---

## Access pattern (required behavior)

- User opens:
  ```
  http://<configured-ip>:<port>/
  ```
- Sees directory listing of folders under `wwwroot`
- Navigates into `<user_name>/`
- Clicks the APK file
- APK downloads and installs

This must work using a standard Android browser.

---

## Configuration (mandatory)

### appsettings.json format (exact)

```json
{
  "appSettings": {
    "BaseUrl": "http://192.168.1.123:8088"
  }
}
```

Rules:
- `BaseUrl` is **authoritative**
- Server must bind **only** to the IP/port specified here
- **Do NOT** bind to `0.0.0.0`
- **Do NOT** use `launchSettings.json` for runtime behavior

---

## Configuration loading (critical rule)

CC must follow **the exact same configuration + logging pipeline** as used in:

```
C:\GitHub\Softellect\Apps\Vpn\VpnClient\Program.fs
```

Specifically:
- Use the same Generic Host / builder pattern
- Reuse existing configuration wiring
- Reuse `log4net.config`
- **Do NOT read appsettings.json directly**
- **Do NOT invent a new configuration mechanism**

---

## Project file (.fsproj) constraints (hard requirement)

The `.fsproj` for `VpnApkServer` must:

- Be structurally identical to:
  ```
  C:\GitHub\Softellect\Apps\Vpn\VpnClient\VpnClient.fsproj
  ```
- Copy:
  - Top-level `PropertyGroup`
  - `PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|x64'"`
  - `PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|x64'"`
- Target **x64 only**
- Output type, framework, runtime identifiers must match the pattern exactly

Only project-specific references and source files may differ.

---

## Web server behavior (authoritative)

CC must implement:

1. **Static file serving** from `wwwroot`
2. **Directory browsing enabled**
3. Correct APK download behavior:
   - Content-Type:  
     `application/vnd.android.package-archive`
   - Downloadable (not rendered)
4. Minimal pipeline:
   - No MVC
   - No Razor
   - No controllers
   - Static files + directory browsing only

---

## Logging

- Reuse existing `log4net.config`
- Log at startup:
  - Bound BaseUrl
  - Absolute path of `wwwroot`
- Log fatal startup failures (invalid BaseUrl, bind failure, etc.)
- Note that the EXE logs to console

---

## Networking constraints

- Server must bind to the **single IP** specified in `BaseUrl`
- All devices are assumed to be on the same router
- No multicast, discovery, or broadcast logic

---

## Deliverables (from CC)

CC must produce:

1. New project under `VpnApkServer`
2. `.fsproj` matching `VpnClient` structure
3. `Program.fs` wired via Generic Host
4. Reused `appsettings.json` and `log4net.config`
5. Static-file + directory-browsing web server
6. Short summary describing:
   - Where `BaseUrl` is read from
   - How binding is enforced
   - How directory browsing is enabled

---

## Acceptance criteria

- Running `VpnApkServer.exe` starts the server
- Server binds only to configured IP/port
- Visiting root URL lists folders
- Navigating folders shows APK
- APK downloads and installs on Android
- No IIS, no HTTPS, no service install, no manual config parsing

---

**This specification is authoritative.  
If CC encounters any ambiguity or deviation from the existing VpnClient host/config pattern, it must stop and ask.**
