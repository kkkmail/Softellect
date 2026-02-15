# Softellect VNC - Feasibility Analysis & Recommendations

**Date:** 2026-02-15
**Status:** Draft v2

---

## 1. Goal

Build a private, simple remote desktop system (codename: **SoftellectVnc**) with:

- Full remote desktop (screen capture + display)
- Keyboard, mouse, and clipboard support
- File transfer with a FAR-like dual-panel file manager (INS sticky selection, grey + pattern selection)
- Pre-login access (runs as a Windows service under SYSTEM)
- NAT traversal via a repeater (public-IP relay server)
- Pre-shared key authentication (machines are pre-authorized, registered by name)
- Viewer shows all known machines and their online/offline status on startup
- Mostly F# with small C# interop for low-level Win32 APIs

Non-goals: multi-monitor negotiation, audio, printing, chat, scaling to thousands of concurrent sessions, cross-platform viewer (Windows-only is fine for now).

**Linux repeater:** The repeater will be written in a Linux-friendly way (no Win32 dependencies, `#if LINUX` conditionals for service hosting) but the first iteration targets Windows only. Linux deployment is a follow-up.

---

## 2. Existing Infrastructure Audit

### 2.1 What already exists and maps directly

| Capability | Existing Code | Reuse Assessment |
|---|---|---|
| **UDP data plane** | `Vpn/Core/UdpProtocol.fs` - push datagrams with AES per-packet encryption, bounded packet queues, reassembly, keepalive | **Direct reuse.** Screen frame data goes over UDP. The `BoundedPacketQueue`, `buildPushDatagram`, `tryParsePushDatagram`, `derivePacketAesKey` are all directly applicable. |
| **WCF control plane** | `Wcf/` - CoreWCF wrapper with NetTcp/Http bindings, FsPickler binary+gzip serialization, `tryReply`/`tryCommunicate` pattern | **Direct reuse.** Auth handshake, session management, clipboard sync, file transfer control messages all go here. |
| **Pre-shared key auth** | `Sys/Crypto.fs` - RSA 4096-bit keys, AES hybrid encryption, sign+encrypt/decrypt+verify. `Vpn/Core/KeyManagement.fs` - key import/export. `Vpn/Server/WcfServer.fs` - `tryDecryptAndVerifyRequest`/`trySignAndEncryptResponse` pattern. | **Direct reuse.** The exact same auth flow (client sends encrypted+signed request, server decrypts with private key, verifies with client public key, responds encrypted with client public key) works for VNC. |
| **Windows service hosting** | `Wcf/Program.fs` - `wcfMain` with `Host.CreateDefaultBuilder().UseWindowsService()`, `IHostedService` pattern. `Sys/ServiceInstaller.fs` - install/uninstall/start/stop. | **Direct reuse.** The VNC service host follows the same pattern as `VpnServer`. |
| **PowerShell deployment** | `Scripts/` - `Reinstall-WindowsService.ps1`, `Install-DistributedService.ps1`, `Start/Stop-WindowsService.ps1`, `VpnServerFunctions.ps1` pattern. | **Direct reuse.** Copy the VPN PS script pattern for VNC service deployment. |
| **C# interop pattern** | `Vpn/Interop/` - C# project with `AllowUnsafeBlocks`, P/Invoke declarations, `FSharpResult<T,string>` return types, consumed by F# `Core/` project. | **Direct reuse as a template.** The VNC interop project follows this exact pattern: C# project for Win32 P/Invoke, F# project wraps it. |
| **Error handling** | `Sys/Errors.fs` - hierarchical `SysError` DU, `Sys/Rop.fs` - railway-oriented programming | **Direct reuse.** Add `VncErr` case to `SysError` or define `VncError` DU in `Vnc/Core/Errors.fs` following the VPN pattern. |
| **Named connections config** | `Vpn/Core/AppSettings.fs` - `ConfigSection.vpnConnections`, `loadVpnConnections()` reads named entries from a JSON section. E.g. `"vpnConnections": { "VPN Connection 1": "...", "VPN Connection 2": "..." }` | **Direct reuse.** VNC machines will be registered the same way: a `"vncMachines"` section in the viewer's `appsettings.json` with named entries. See Section 3.5. |
| **Configuration** | `Sys/AppSettings.fs` - JSON-based config with `parseSimpleSetting`, `ConfigKey` pattern | **Direct reuse.** |
| **Logging** | `Sys/Logging.fs` - log4net wrapper | **Direct reuse.** |
| **Serialization** | `Sys/Core.fs` - `trySerialize`/`tryDeserialize` with `BinaryZippedFormat` | **Direct reuse.** For file transfer chunks and clipboard data. |

### 2.2 What needs to be built

| Component | Complexity | Notes |
|---|---|---|
| **Screen capture (DXGI)** | Medium | C# interop. Desktop Duplication API. ~300-500 lines of C#. |
| **Screen encoding** | Medium | Dirty-rect delta encoding + compression. F#. |
| **Input injection** | Low | C# interop. `SendInput` Win32 API. ~100-150 lines of C#. |
| **Clipboard sync** | Low | `GetClipboardData`/`SetClipboardData` or .NET `Clipboard` class. |
| **File manager (FAR-like)** | Medium | Dual-panel WinForms UI with INS selection, grey + pattern, chunked transfer over WCF. |
| **Repeater service** | Medium | UDP relay + WCF relay on public-IP server. Written Linux-friendly, deployed on Windows first. |
| **Viewer application** | Medium | WinForms app: machine list with status, renders frames, captures input. |
| **Session management** | Low | Follows existing VPN `AuthService`/`ClientRegistry` pattern closely. |

---

## 3. Architecture

### 3.1 Components

```
+------------------+         +-------------------+        +------------------+
|   VNC Viewer     |         |  VNC Repeater     |        |  VNC Service     |
|   (WinForms)     |         |  (Windows; Linux- |        |  (Win Service)   |
|                  |         |   friendly code)   |        |  on target host  |
|  Machine list    |         |  Public IP         |        |                  |
|  + status        |         |                    |        |                  |
|  Renders screen  |<--UDP-->|  Relays UDP+WCF   |<--UDP->|  DXGI capture    |
|  Sends input     |--WCF-->|  between viewer    |--WCF-->|  SendInput       |
|  FAR file mgr    |         |  and service       |        |  Clipboard       |
+------------------+         +-------------------+        +------------------+
```

### 3.2 Data Flows

**Screen capture flow (high-bandwidth, UDP):**
```
VNC Service                          Repeater                    VNC Viewer
    |                                    |                          |
    |-- DXGI captures dirty rects ------>|                          |
    |-- Encode (compress dirty rects) -->|                          |
    |-- UDP push datagram -------------->|-- relay ---------------->|
    |                                    |                          |-- Decode
    |                                    |                          |-- Render
```

**Input flow (low-bandwidth, WCF via repeater):**
```
VNC Viewer                           Repeater                    VNC Service
    |                                    |                          |
    |-- Mouse/key event (WCF) --------->|-- relay ---------------->|
    |                                    |                          |-- SendInput
```

**File transfer flow (WCF via repeater):**
```
Either direction, chunked over WCF, FsPickler serialized + gzipped.
Directory listing requests and file chunks both go over WCF.
```

**Clipboard flow (WCF via repeater):**
```
Bidirectional. Clipboard change detected -> serialize -> WCF -> apply on remote.
```

### 3.3 Repeater Design

The repeater is the NAT traversal solution. It is the equivalent of UltraVNC's repeater but integrated into your stack.

**How it works:**
1. VNC Service (behind NAT) connects **outbound** to the repeater on startup, establishing a persistent WCF control connection and registering its machine name.
2. VNC Service also sends UDP keepalives to the repeater, establishing the NAT mapping for the UDP data plane (same pattern as your VPN client's keepalive).
3. VNC Viewer connects to the repeater, specifies which machine name it wants to reach.
4. Repeater authenticates the viewer (pre-shared keys), then relays both WCF and UDP traffic between viewer and service.

**Key insight:** The VNC Service is the "client" of the repeater (it connects outbound). The viewer is also a "client" of the repeater. The repeater just pairs them. This is architecturally identical to how your VPN clients connect to the VPN server - the repeater IS the VPN server equivalent, but instead of routing IP packets to the internet, it routes VNC frames between paired endpoints.

**Linux-friendly design:** The repeater code will avoid Win32 dependencies and use `#if LINUX` conditionals only for service hosting differences (same pattern as `Wcf/Program.fs`). All repeater logic (UDP relay, WCF relay, session pairing) uses cross-platform .NET APIs: `System.Net.Sockets.UdpClient`, CoreWCF, standard collections. The first iteration deploys on Windows; Linux deployment follows once the Windows version is stable.

### 3.4 Pre-Login Access

Running as a Windows service under `NT AUTHORITY\SYSTEM` (same as your VPN server, see `VpnServerFunctions.ps1` line 24: `Install-DistributedService -ServiceName $ServiceName -Login "NT AUTHORITY\SYSTEM"`) provides:

- Starts before any user logs in
- Access to the secure desktop (Ctrl+Alt+Del, login screen)
- Survives user logoff/switch

For DXGI Desktop Duplication to work from a service on the secure desktop (pre-login, UAC prompts), the service must run in **Session 0** (which Windows services do by default) and needs to handle desktop switching. Specifically:

- **Session 0 isolation:** Since Vista, services run in Session 0 which has no interactive desktop. DXGI Desktop Duplication requires access to the active console session's desktop.
- **Solution:** The service spawns a helper process in the **console session** (Session 1+) using `CreateProcessAsUser` or `WTSQueryUserToken` + `CreateProcessAsUser`. This helper does the actual DXGI capture and communicates with the service via a local named pipe or shared memory. This is the pattern UltraVNC uses - its `winvnc.exe` service spawns a helper into the active session and monitors session change events (`SERVICE_CONTROL_SESSIONCHANGE`) to relaunch the helper when sessions switch (login/logoff/fast user switching).
- **For the login screen:** Detect `WTS_CONSOLE_CONNECT` / `WTS_SESSION_LOGOFF` events. When no user is logged in, the console session is the Winlogon desktop in Session 1. The helper process launched via `CreateProcessAsUser` with the SYSTEM token can access this desktop.
- **Alternative (simpler, slightly less capable):** Use `BitBlt` from the service process directly. This works for basic screen capture without GPU acceleration but does not capture hardware-accelerated content (DirectX overlays, video playback). For remote administration purposes this is often sufficient.

**Recommendation:** Start with the helper-process approach. It is the proven pattern used by UltraVNC and other tools for Session 0 service + interactive desktop capture.

### 3.5 Machine Registration & Status

Machines are registered by name in the viewer's `appsettings.json`, following the same pattern as VPN connections (`Vpn/Core/AppSettings.fs` - `loadVpnConnections()` reads from the `"vpnConnections"` JSON section):

```json
{
  "appSettings": {
    "RepeaterAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=repeater.example.com;netTcpServicePort=5090;netTcpServiceName=VncRepeater;netTcpSecurityMode=NoSecurity",
    "ViewerKeyPath": "C:\\Keys\\VncViewer",
    "RepeaterPublicKeyPath": "C:\\Keys\\VncRepeater"
  },
  "vncMachines": {
    "Office Desktop": "machineName=Office Desktop;machineId={aaaa-bbbb-...}",
    "Home Server": "machineName=Home Server;machineId={cccc-dddd-...}",
    "Lab PC": "machineName=Lab PC;machineId={eeee-ffff-...}"
  }
}
```

**Viewer startup behavior:**
1. Load machine list from `"vncMachines"` section (same `AppSettingsProvider.tryCreate ConfigSection.vncMachines` + `tryGetSectionKeys` pattern as `loadVpnConnections`).
2. Display all machines in a list/grid.
3. For each machine, query the repeater for its online/offline status (lightweight WCF call).
4. Show status indicator next to each machine name (online/offline/unknown).
5. User double-clicks or selects a machine to connect.

The VNC Service's `appsettings.json` mirrors this with its own machine name:

```json
{
  "appSettings": {
    "VncMachineName": "Office Desktop",
    "VncMachineId": "{aaaa-bbbb-...}",
    "RepeaterAccessInfo": "NetTcpServiceInfo|...",
    "ServiceKeyPath": "C:\\Keys\\VncService",
    "RepeaterPublicKeyPath": "C:\\Keys\\VncRepeater"
  }
}
```

---

## 4. C# Interop Components

Following the `Vpn/Interop/` pattern: a single C# project `Vnc/Interop/Interop.csproj` with `AllowUnsafeBlocks=true`, returning `FSharpResult<T, string>`.

### 4.1 Screen Capture (`DesktopDuplication.cs`)

**Win32/COM APIs needed:**
- `IDXGIOutputDuplication` (DXGI 1.2 Desktop Duplication API)
- `ID3D11Device`, `ID3D11DeviceContext`
- `AcquireNextFrame`, `MapSubresource` for pixel data
- `GetFrameDirtyRects`, `GetFrameMoveRects` for delta information

**What it provides to F#:**
```
CaptureFrame() -> FSharpResult<FrameData, string>

FrameData:
  - Width, Height, Stride
  - PixelData: byte[]  (or Memory<byte> for zero-copy)
  - DirtyRects: Rectangle[]
  - MoveRects: MoveRectangle[]
  - CursorPosition: Point
  - CursorShape: byte[] (optional)
```

**Size estimate:** ~400-500 lines of C#. Well-documented API with many reference implementations.

**UltraVNC note:** UltraVNC historically used a custom mirror driver for efficient capture. Since Windows 8, DXGI Desktop Duplication replaced that. UltraVNC's newer versions use DXGI. Their implementation confirms that DXGI is the right choice - it provides hardware-accelerated capture with dirty rect tracking built into the API. No need for a mirror driver.

### 4.2 Input Injection (`InputInjector.cs`)

**Win32 APIs needed:**
- `SendInput` with `INPUT_MOUSE`, `INPUT_KEYBOARD`
- `MapVirtualKey` for scan code translation

**What it provides to F#:**
```
SendMouseEvent(x, y, buttons, wheel) -> FSharpResult<Unit, string>
SendKeyboardEvent(virtualKey, scanCode, isKeyUp, isExtended) -> FSharpResult<Unit, string>
```

**Size estimate:** ~100-150 lines of C#. Straightforward.

### 4.3 Clipboard (`ClipboardInterop.cs`)

**Approach:** Use `System.Windows.Forms.Clipboard` (available in .NET on Windows) or P/Invoke `OpenClipboard`/`GetClipboardData`/`SetClipboardData` for format control.

**What it provides to F#:**
```
GetClipboardContent() -> FSharpResult<ClipboardData, string>
SetClipboardContent(ClipboardData) -> FSharpResult<Unit, string>
AddClipboardListener(callback) -> FSharpResult<Unit, string>

ClipboardData:
  - Text: string option
  - Files: string[] option  (for file paths on clipboard)
```

**Size estimate:** ~100-200 lines of C#.

### 4.4 Session Helper (`SessionHelper.cs`)

**Win32 APIs needed:**
- `WTSGetActiveConsoleSessionId`
- `WTSQueryUserToken`
- `CreateProcessAsUser` / `DuplicateTokenEx`
- `WTSRegisterSessionNotification` / `WM_WTSSESSION_CHANGE`

**What it provides:**
- Launches the capture helper process in the active console session
- Detects session switches (login/logoff/lock/unlock)

**UltraVNC note:** UltraVNC's service (`winvnc.exe`) handles `SERVICE_CONTROL_SESSIONCHANGE` to detect when the active console session changes. On each change, it kills the old helper and spawns a new one in the new session. This is essential for supporting fast user switching and the login screen. The same pattern applies here.

**Size estimate:** ~200-300 lines of C#.

---

## 5. F# Components

### 5.1 Project Structure

```
Vnc/
  Core/
    Core.fsproj          -- References: Sys, Wcf
    Primitives.fs        -- VncMachineName, VncMachineId, VncSessionId, FrameData, InputEvent, etc.
    Errors.fs            -- VncError DU
    ServiceInfo.fs       -- IVncService, IVncWcfService contracts
    AppSettings.fs       -- Configuration loading (vncMachines section, etc.)
    Protocol.fs          -- Frame encoding/decoding, delta compression
    FileSystemTypes.fs   -- Directory listing types, file metadata for FAR-like file manager
  Interop/
    Interop.csproj       -- C# interop (DXGI, SendInput, Clipboard, Session)
  Service/
    Service.fsproj       -- References: Core, Interop, Wcf
    CaptureService.fs    -- DXGI frame capture loop
    InputService.fs      -- Input injection handler
    ClipboardService.fs  -- Clipboard sync
    FileService.fs       -- Remote file system browsing + file transfer handler
    VncService.fs        -- Main service orchestrator (IHostedService)
    WcfServer.fs         -- WCF service implementation (encrypted)
    Program.fs           -- Service entry point (wcfMain pattern)
  Repeater/
    Repeater.fsproj      -- References: Core, Wcf (no Win32 dependencies)
    RepeaterService.fs   -- Pairs viewer<->service, relays traffic, tracks machine status
    UdpRelay.fs          -- UDP packet relay
    WcfRelay.fs          -- WCF message relay
    Program.fs           -- Repeater entry point (#if LINUX for hosting)
  Viewer/
    Viewer.fsproj        -- References: Core, Wcf (WinForms app)
    MachineListForm.fs   -- Startup form: shows all known machines + status
    ScreenRenderer.fs    -- Renders received frames
    InputCapture.fs      -- Captures local mouse/keyboard
    ClipboardSync.fs     -- Local clipboard monitoring
    FileManagerForm.fs   -- FAR-like dual-panel file transfer UI
    ViewerForm.fs        -- Main remote desktop form
    Program.fs           -- Viewer entry point
Apps/
  Vnc/
    VncService/          -- Publishable Windows service
    VncRepeater/         -- Publishable repeater (Windows first, Linux-ready)
    VncViewer/           -- Publishable viewer app
```

### 5.2 Key Types (Primitives.fs)

```fsharp
type VncMachineName = VncMachineName of string  // Human-readable name ("Office Desktop")
type VncMachineId = VncMachineId of Guid        // Identifies a remote machine
type VncSessionId = VncSessionId of Guid        // Active viewer<->service session

type VncMachineStatus =
    | Online
    | Offline
    | Unknown

type VncMachineInfo =
    { machineName : VncMachineName
      machineId : VncMachineId
      status : VncMachineStatus }

type VncRepeaterAddress =
    { address : ServiceAddress
      udpPort : ServicePort
      wcfPort : ServicePort }

type FrameRegion =
    { x: int; y: int; width: int; height: int; data: byte[] }

type FrameUpdate =
    { sequenceNumber: uint64
      regions: FrameRegion[]
      cursorX: int; cursorY: int
      cursorShape: byte[] option }

type InputEvent =
    | MouseMove of x: int * y: int
    | MouseButton of x: int * y: int * button: MouseButton * isDown: bool
    | MouseWheel of x: int * y: int * delta: int
    | KeyPress of virtualKey: int * scanCode: int * isDown: bool * isExtended: bool

type ClipboardData =
    | TextClip of string
    | FileListClip of string[]
```

### 5.3 File System & Transfer Types (FileSystemTypes.fs)

```fsharp
type FileEntryKind =
    | FileEntry
    | DirectoryEntry
    | ParentDirectory   // ".." entry

type FileEntry =
    { name : string
      kind : FileEntryKind
      size : int64
      lastModified : DateTime
      isSelected : bool }

type DirectoryListing =
    { path : string
      entries : FileEntry[]
      error : string option }

type FileTransferId = FileTransferId of Guid

type FileTransferDirection =
    | LocalToRemote
    | RemoteToLocal

type FileChunk =
    { transferId : FileTransferId
      filePath : string
      chunkIndex : int
      totalChunks : int
      data : byte[] }

type FileTransferRequest =
    { transferId : FileTransferId
      direction : FileTransferDirection
      files : string list       // Selected file paths
      destinationPath : string }
```

### 5.4 WCF Service Contracts (ServiceInfo.fs)

Following the VPN's `IAuthWcfService` pattern (byte[] -> byte[] with encryption):

```fsharp
[<ServiceContract(ConfigurationName = "VncService")>]
type IVncWcfService =
    [<OperationContract>] abstract authenticate : data:byte[] -> byte[]
    [<OperationContract>] abstract sendInput : data:byte[] -> byte[]
    [<OperationContract>] abstract getClipboard : data:byte[] -> byte[]
    [<OperationContract>] abstract setClipboard : data:byte[] -> byte[]
    [<OperationContract>] abstract listDirectory : data:byte[] -> byte[]
    [<OperationContract>] abstract startFileTransfer : data:byte[] -> byte[]
    [<OperationContract>] abstract sendFileChunk : data:byte[] -> byte[]
    [<OperationContract>] abstract receiveFileChunk : data:byte[] -> byte[]

[<ServiceContract(ConfigurationName = "VncRepeaterService")>]
type IVncRepeaterWcfService =
    [<OperationContract>] abstract registerMachine : data:byte[] -> byte[]
    [<OperationContract>] abstract connectToMachine : data:byte[] -> byte[]
    [<OperationContract>] abstract getMachineStatus : data:byte[] -> byte[]
    [<OperationContract>] abstract relayToService : data:byte[] -> byte[]
    [<OperationContract>] abstract relayToViewer : data:byte[] -> byte[]
```

### 5.5 Frame Encoding (Protocol.fs)

**Strategy:** Dirty-rect encoding with compression.

1. DXGI gives us dirty rectangles (regions that changed since last frame).
2. For each dirty rect, extract pixel data from the frame buffer.
3. Compress each region with GZip (already used in the codebase via `BinaryZippedFormat`).
4. Serialize the `FrameUpdate` record.
5. Fragment into UDP push datagrams using the existing `buildPushDatagram` with reassembly on the viewer side.

**Bandwidth estimate:** A typical desktop at 1920x1080x32bpp = ~8MB/frame. With dirty rects (typically 1-10% of screen changes per frame), you're sending ~80KB-800KB per update. With compression, this drops to ~20KB-200KB. At 30fps with moderate changes, expect 1-6 Mbps sustained - well within your UDP plane's capacity.

**Adaptive quality:** Can optionally add JPEG/WebP encoding for dirty rects when bandwidth is constrained. This is an optimization, not required for v1.

**UltraVNC encoding note:** UltraVNC supports multiple RFB encodings (Raw, CopyRect, RRE, Hextile, Zlib, Tight, Ultra, Ultra2). The most relevant insight: their "Ultra" encoding is essentially zlib-compressed dirty rectangles with optional JPEG for photographic regions - which is exactly the strategy outlined here. Their CopyRect encoding (for moved regions) is worth noting: DXGI provides `MoveRects` that describe screen-to-screen copies (e.g., scrolling), and these can be sent as just coordinates (x,y,w,h,srcX,srcY) instead of pixel data, saving significant bandwidth during scrolling.

### 5.6 FAR-Like File Manager (FileManagerForm.fs)

The file manager uses a dual-panel layout inspired by FAR Manager / Norton Commander:

```
+-----------------------------------+-----------------------------------+
| LOCAL (C:\Users\me\Documents)     | REMOTE (C:\Users\admin\Desktop)   |
+-----------------------------------+-----------------------------------+
| [..] <DIR>              2026-02-15|  [..] <DIR>              2026-02-15|
|  [Projects] <DIR>       2026-02-14| *[Backups] <DIR>         2026-02-13|
| *readme.txt     1,234   2026-02-10|  config.json     567     2026-02-12|
| *data.csv      45,678   2026-02-09| *report.pdf   12,345     2026-02-11|
|  notes.md       2,345   2026-02-08|  log.txt      89,012     2026-02-10|
|                                   |                                   |
+-----------------------------------+-----------------------------------+
| F5 Copy  F6 Move  F7 MkDir  F8 Delete  INS Select  Grey+ Pattern     |
+-----------------------------------------------------------------------+
```

**Selection model (FAR-style, not Explorer-style):**
- **INS** toggles selection on the current item and moves cursor down (sticky - stays selected as you navigate)
- **Grey +** (numpad plus, or via menu) opens a pattern dialog (e.g., `*.txt`, `data*`) to select matching files
- **Grey -** (numpad minus, or via menu) deselects by pattern
- **Grey *** (numpad asterisk, or via menu) inverts selection
- Selected items are marked with `*` prefix (or highlighted)
- Selection persists while navigating - this is the key difference from Explorer where clicking clears selection

**Operations:**
- **F5** Copy selected files from active panel to the other panel's current directory
- **F6** Move selected files
- **F7** Create directory
- **F8** Delete selected files (with confirmation)
- **Enter** on a directory navigates into it; on `..` navigates up
- **Tab** switches active panel

**Implementation:**
- Left panel: local file system (standard `System.IO.Directory.GetFiles`/`GetDirectories`)
- Right panel: remote file system (via `listDirectory` WCF call to the VNC Service)
- Transfer: chunked over WCF using `FileChunk` records, FsPickler serialized + gzipped
- Progress bar during transfer
- WinForms `DataGridView` or custom-drawn `Panel` for each panel

---

## 6. Repeater Deep Dive

### 6.1 Registration Flow

```
1. VNC Service starts, reads config: repeaterAddress, machineName, machineId, pre-shared keys.
2. VNC Service connects to Repeater via WCF (outbound TCP):
   - Sends encrypted+signed registration: { machineName, machineId, publicKey }
   - Repeater verifies with stored public key, responds with OK + UDP port.
3. VNC Service begins sending UDP keepalives to Repeater's UDP port.
   - This establishes and maintains the NAT pinhole.
4. Repeater stores: machineName -> (machineId, WCF channel, UDP endpoint, lastSeen).
5. Repeater marks machine as Online. If keepalives stop, marks as Offline.
```

### 6.2 Viewer Connection Flow

```
1. Viewer starts, loads machine list from appsettings.json "vncMachines" section.
2. Viewer queries Repeater for status of all known machines (batch WCF call).
3. Viewer displays machine list with Online/Offline indicators.
4. User selects a machine to connect.
5. Viewer connects to Repeater via WCF:
   - Sends encrypted+signed connect request: { targetMachineName, viewerPublicKey }
   - Repeater checks: is targetMachine registered and online?
   - If yes: creates a VncSessionId, notifies the VNC Service via its WCF channel.
   - Responds to Viewer with: { sessionId, UDP port for frame data }
6. Viewer begins receiving UDP frame data relayed through Repeater.
7. Viewer sends input events via WCF through Repeater.
```

### 6.3 Relay Mechanism

**UDP relay:** Repeater receives UDP datagrams from VNC Service (screen frames), looks up the paired viewer by sessionId, and forwards them. Vice versa for any viewer->service UDP traffic. This is a simple packet-forwarding loop, no decryption needed (end-to-end encryption between viewer and service is optional since the transport is already encrypted per-packet with AES).

**WCF relay:** Repeater exposes a WCF service that both viewer and service connect to. For input events and control messages, the repeater receives from the viewer and forwards to the service's WCF callback or queues them for the service to poll.

**UltraVNC repeater comparison:** UltraVNC's repeater (`repeater.exe`) is a simple TCP proxy that pairs connections by an ID number. The VNC server connects to the repeater and sends its ID; the viewer connects and requests the same ID; the repeater then bridges the two TCP streams byte-for-byte. Our repeater is more sophisticated (separate UDP + WCF planes, encrypted auth, machine name registry with status tracking) but the core principle is the same: a rendezvous point for two endpoints behind NAT.

### 6.4 Linux Compatibility

The repeater needs only:
- CoreWCF (works on Linux - already used in `VpnServerLinux`)
- `System.Net.Sockets.UdpClient` (cross-platform)
- No Win32 APIs

The repeater code will use `#if LINUX` only for the hosting difference (`UseWindowsService()` vs systemd / plain console), matching the existing `Wcf/Program.fs` pattern. All relay logic, session pairing, and machine registry use cross-platform .NET APIs.

**First iteration:** Windows deployment only. Linux deployment follows once the architecture is validated.

---

## 7. Build Order & Milestones

### Phase 1: Screen Capture Proof of Concept
1. `Vnc/Interop/` - DXGI Desktop Duplication wrapper in C#
2. `Vnc/Core/` - Frame data types, basic encoding (dirty rects + GZip)
3. Simple console app: capture screen, encode, decode, display in a WinForms window on the same machine
4. **Validates:** DXGI works, encoding pipeline, rendering

### Phase 2: Direct Connection
5. `Vnc/Service/` - Windows service with DXGI capture loop + UDP sender
6. `Vnc/Viewer/` - WinForms app with UDP receiver + renderer
7. Input injection (SendInput interop) + input event WCF contract
8. Direct LAN connection (no repeater, no NAT): Viewer connects to Service by IP
9. **Validates:** End-to-end remote desktop over LAN

### Phase 3: Auth & Encryption
10. Pre-shared key auth (reuse VPN's `tryDecryptAndVerifyRequest` pattern)
11. Session establishment over WCF
12. Per-packet AES on UDP frames (reuse `derivePacketAesKey`)
13. **Validates:** Secure connection

### Phase 4: Repeater
14. `Vnc/Repeater/` - Registration, pairing, UDP relay, WCF relay, machine status tracking
15. VNC Service connects outbound to repeater
16. Viewer connects to repeater, reaches service behind NAT
17. Machine list with online/offline status in viewer
18. **Validates:** NAT traversal, full architecture

### Phase 5: Clipboard & File Transfer
19. Clipboard sync (bidirectional, text + file list)
20. FAR-like dual-panel file manager with INS selection, grey + pattern, F5 copy
21. Chunked file transfer over WCF
22. **Validates:** Complete feature set

### Phase 6: Pre-Login & Polish
23. Session 0 helper process for secure desktop capture
24. PowerShell install/uninstall scripts (clone VPN pattern)
25. Reconnection logic, error recovery
26. **Validates:** Production-ready service

---

## 8. Feasibility Assessment

### Verdict: Highly Feasible

**Rationale:**

1. **~70% of the infrastructure already exists.** The UDP data plane, WCF control plane, crypto, auth, service hosting, deployment scripts, error handling, logging, and configuration are all production-tested in the VPN codebase. This is not a greenfield project - it is extending an existing platform with a new application (remote desktop instead of VPN tunneling).

2. **The new code is well-bounded.** The genuinely new pieces are:
   - DXGI screen capture (~500 lines C#)
   - SendInput injection (~150 lines C#)
   - Frame encoding (~300 lines F#)
   - Viewer rendering (~400 lines F#)
   - Repeater relay logic (~500 lines F#)
   - FAR-like file manager (~600 lines F#)
   - Machine list + status UI (~200 lines F#)
   - Total: ~2500-3000 lines of new code, not including tests and configuration

3. **No exotic dependencies.** DXGI Desktop Duplication is a stable, well-documented Windows API available since Windows 8. SendInput has been stable since Windows 2000. CoreWCF on Linux is proven in your VPN server. No third-party screen capture libraries needed.

4. **The repeater is architecturally simple.** It is essentially a matchmaker + packet forwarder. Your VPN server already does the hard part (UDP relay between clients). The repeater is a stripped-down version that pairs two endpoints instead of routing to the internet.

5. **Pre-login access is the hardest part**, but it's a solved problem. The Session 0 helper-process pattern is well-documented and used by UltraVNC, TightVNC, and others. The service itself already runs under SYSTEM (your VPN uses this).

### Risks

| Risk | Severity | Mitigation |
|---|---|---|
| DXGI Desktop Duplication on secure desktop (pre-login) | Medium | Phase 6. Start with logged-in desktop first. Helper-process pattern is well-documented. UltraVNC uses exactly this pattern. |
| Frame encoding performance at high resolution / high change rate | Low | DXGI dirty rects reduce data volume drastically. GZip compression is fast. CopyRect optimization for scrolling. Adaptive quality (JPEG for large rects) is an easy fallback. |
| Repeater throughput for screen data | Low | Screen data is 1-6 Mbps per session. A server with 100 Mbps easily handles dozens of simultaneous sessions. Your UDP relay is already efficient. |
| Clipboard with large content (images) | Low | Cap clipboard sync to text and file paths initially. Image clipboard is a v2 feature. |
| CoreWCF stability for long-running relay connections | Low | Your VPN already runs CoreWCF connections 24/7. The WCF layer is battle-tested. |

---

## 9. Recommendations

1. **Start with Phase 1 (DXGI PoC).** The screen capture C# interop is the single piece with the most unknowns. Validate it first in isolation before building the full pipeline.

2. **Reuse, don't rewrite.** Reference `Softellect.Sys`, `Softellect.Wcf`, and `Softellect.Vpn.Core` directly. The VPN's UDP protocol, crypto, and service hosting code should be consumed as library dependencies, not copied.

3. **Follow the VPN's architectural pattern exactly.** The VNC project structure should mirror the VPN:
   - `Vnc/Core/` = `Vpn/Core/` (primitives, errors, protocol, app settings)
   - `Vnc/Interop/` = `Vpn/Interop/` (C# P/Invoke)
   - `Vnc/Service/` = `Vpn/Server/` (the remote host agent)
   - `Vnc/Repeater/` = new, but follows `Vpn/Server/` hosting pattern, Linux-friendly
   - `Vnc/Viewer/` = new, follows `Vpn/Client/` + WinForms UI
   - `Apps/Vnc/*` = `Apps/Vpn/*` (thin entry points + PS scripts)

4. **Use GZip for frame compression initially** since it's already integrated (`BinaryZippedFormat`). Switch to LZ4 or Zstd only if profiling shows GZip is the bottleneck (unlikely for dirty-rect data).

5. **For the repeater, reuse the VPN server's `UdpServer.fs` pattern** for the UDP relay loop and `WcfServer.fs` for the authenticated WCF endpoint. The repeater is a VPN server that routes VNC frames instead of IP packets.

6. **Implement CopyRect optimization from the start.** DXGI `MoveRects` are free metadata that can save significant bandwidth during scrolling. Send as coordinate tuples instead of pixel data.

7. **Defer multi-monitor support.** DXGI can enumerate outputs, but handling multiple monitors adds complexity to frame encoding and viewer rendering. Start with the primary monitor.

8. **Defer audio.** Not in the requirements, and it's a separate, complex pipeline (WASAPI capture + Opus encoding + jitter buffer). Can be added later without architectural changes.

---

## 10. UltraVNC Patterns Worth Adopting

Based on examination of UltraVNC's architecture:

1. **Session 0 service + helper process pattern.** UltraVNC's `winvnc.exe` runs as a service, spawns a helper process in the active console session for screen capture and input injection. Handles `SERVICE_CONTROL_SESSIONCHANGE` to relaunch the helper on session switches. This is the proven approach for pre-login access.

2. **DXGI Desktop Duplication over mirror driver.** UltraVNC's older versions used a custom mirror driver (`video hook driver`) for efficient capture. Modern versions use DXGI Desktop Duplication which is more stable and does not require driver installation. We skip the mirror driver entirely.

3. **Repeater as a simple rendezvous proxy.** UltraVNC's repeater is intentionally minimal: it just bridges two TCP connections by ID. Our repeater is more capable (separate data/control planes, auth, status tracking) but the simplicity principle applies: the repeater should do as little as possible beyond relaying.

4. **File transfer as a separate mode.** UltraVNC's file transfer is a separate UI panel, not integrated into the desktop view. The FAR-like approach is better than their Explorer-style interface, but the separation of concerns (file transfer UI vs desktop view) is worth keeping.

5. **CopyRect encoding.** UltraVNC uses CopyRect extensively for scrolling and window moves. DXGI provides this information for free via `MoveRects`.

6. **Cursor shape tracking.** UltraVNC sends cursor shape changes separately from screen updates. DXGI provides cursor metadata via `AcquireNextFrame`. Sending cursor position + shape as a lightweight message (rather than drawing it into the frame) reduces latency for the most visible element on screen.

**Patterns to avoid from UltraVNC:**
- Their plugin system (unnecessary complexity for a private tool)
- Their chat feature (not needed)
- Their Java viewer (not applicable)
- Their DSM (Data Stream Modifier) plugin encryption (we have a better crypto stack already)

---

## 11. Dependencies Summary

**NuGet packages needed (beyond what's already referenced):**
- None for core functionality. DXGI, SendInput, and clipboard APIs are all in Windows SDK (accessed via P/Invoke or `SharpDX.DXGI` / `Vortice.DirectX` for a cleaner DXGI wrapper).
- Optional: `K4os.Compression.LZ4` for faster frame compression (v2 optimization).
- Optional: `System.Drawing.Common` or `SkiaSharp` for JPEG encoding of dirty rects (v2 optimization).

**Existing Softellect packages reused:**
- `Softellect.Sys` (crypto, errors, logging, config, service installer, core utilities)
- `Softellect.Wcf` (CoreWCF wrapper, client/service, serialization)
- `Softellect.Vpn.Core` (UDP protocol, bounded queues, reassembly - consider extracting the non-VPN-specific parts into `Softellect.Sys` or a shared transport library)

---

## 12. Open Questions for Owner

1. **Viewer technology:** WinForms (simplest, fastest to build) vs WPF (better rendering pipeline, supports GPU composition) vs bare SDL2/OpenGL (lowest latency but more work). **Recommendation: WinForms for v1.**

2. **Frame encoding:** Start with raw dirty rects + GZip, or invest in a codec (JPEG/WebP per rect) from the start? **Recommendation: GZip first, profile later.**

3. **Repeater deployment:** Single repeater, or support for multiple repeaters with failover? **Recommendation: Single repeater for v1.**

4. **Shared transport library:** The UDP push protocol (`UdpProtocol.fs`), `BoundedPacketQueue`, and `derivePacketAesKey` are not VPN-specific. Consider extracting them into `Softellect.Sys` or a new `Softellect.Transport` library so both VPN and VNC reference the same code without VNC depending on `Vpn.Core`. **Recommendation: Yes, do this before starting VNC development.**

5. **DXGI wrapper approach:** Raw P/Invoke in C# vs using `Vortice.DirectX` (a well-maintained, lightweight .NET wrapper for DirectX APIs including DXGI). Vortice would reduce the C# interop code significantly. **Recommendation: Evaluate Vortice - if it provides clean DXGI Desktop Duplication support, it saves writing COM interop by hand.**
