# vpn_gateway_spec__045__report__01.md

## Android Auth/Ping and APK Discovery Report

This report provides a comprehensive analysis of the existing VPN codebase to advise on adding:
1. A separate Android-friendly auth/ping path (TCP-based) from Android app to Windows server
2. An Android client APK using VpnService + TUN that reuses the existing UDP data plane

---

## 1. Inventory: Projects and Key Files

### Project Structure

**Library Projects (under `C:\GitHub\Softellect\Vpn\`):**

| Project | Path | Purpose |
|---------|------|---------|
| **Core** | `Vpn\Core\Core.fsproj` | Shared primitives, protocols, settings, key management |
| **Interop** | `Vpn\Interop\Interop.csproj` | WinTun adapter C# interop, KillSwitch (WFP) |
| **Client** | `Vpn\Client\Client.fsproj` | VPN client logic (WCF auth client, UDP client, Tunnel) |
| **Server** | `Vpn\Server\Server.fsproj` | VPN server logic (WCF server, UDP server, NAT, DNS proxy) |

**Application Projects (under `C:\GitHub\Softellect\Apps\Vpn\`):**

| Project | Path | Purpose |
|---------|------|---------|
| **VpnServer** | `Apps\Vpn\VpnServer\VpnServer.fsproj` | Server executable (Windows Service) |
| **VpnClient** | `Apps\Vpn\VpnClient\VpnClient.fsproj` | Client executable (Windows Service) |
| **VpnServerAdm** | `Apps\Vpn\VpnServerAdm\VpnServerAdm.fsproj` | Server admin CLI (key generation) |
| **VpnClientAdm** | `Apps\Vpn\VpnClientAdm\VpnClientAdm.fsproj` | Client admin CLI (key generation) |

### Key Files by Functional Area

**Core - Shared Types and Protocols:**
- `Core\Primitives.fs` - `VpnClientId`, `VpnServerId`, `VpnSessionId`, `VpnAuthRequest`, `VpnAuthResponse`, `VpnPingRequest`
- `Core\UdpProtocol.fs` - UDP datagram format, AES key derivation, `BoundedPacketQueue`, push commands
- `Core\AppSettings.fs` - Configuration loading (`VpnClientAccessInfo`, `VpnServerAccessInfo`)
- `Core\ServiceInfo.fs` - Service interfaces (`IAuthService`, `IAuthWcfService`, `IVpnPushService`)
- `Core\KeyManagement.fs` - Key import/export functions
- `Core\Errors.fs` - Error type hierarchy

**Server - Auth/Control Plane:**
- `Server\WcfServer.fs` - `AuthWcfService` - CoreWCF service implementing `IAuthWcfService`
- `Server\Service.fs` - `AuthService` (implements `IAuthService`), `VpnPushService` (implements `IVpnPushService`)
- `Server\ClientRegistry.fs` - `ClientRegistry`, `PushClientSession` - session state management
- `Server\Program.fs` - Server startup, key loading, hosted service wiring

**Server - Data Plane:**
- `Server\UdpServer.fs` - `VpnCombinedUdpHostedService` - UDP receive/send loops
- `Server\PacketRouter.fs` - `PacketRouter` - WinTun receive loop, routing decisions
- `Server\Nat.fs` - User-space NAT (TCP/UDP/ICMP), 5-tuple connection tracking
- `Server\ExternalInterface.fs` - `ExternalGateway` - raw socket for internet traffic
- `Server\DnsProxy.fs` - DNS query forwarding to upstream (1.1.1.1)
- `Server\IcmpProxy.fs` - ICMP Echo Request/Reply proxy

**Client - Auth/Control Plane:**
- `Client\WcfClient.fs` - `AuthWcfClient` - WCF client for auth/ping
- `Client\Service.fs` - `VpnPushClientService` - supervisor loop, auth state management

**Client - Data Plane:**
- `Client\UdpClient.fs` - `VpnPushUdpClient` - UDP send/receive/keepalive loops
- `Client\Tunnel.fs` - `Tunnel` - WinTun adapter management, packet capture

**Interop (C#):**
- `Interop\WinTunAdapter.cs` - WinTun adapter wrapper
- `Interop\KillSwitch.cs` - Windows Filtering Platform (WFP) based kill-switch

---

## 2. Current Flows: Windows Auth/Ping and UDP Data Plane

### Authentication Flow (CoreWCF over TCP)

```
+----------------+                                   +----------------+
|  Windows       |                                   |  Windows       |
|  VPN Client    |                                   |  VPN Server    |
+----------------+                                   +----------------+
        |                                                    |
        |  1. Create VpnAuthRequest                          |
        |     {clientId, timestamp, nonce}                   |
        |                                                    |
        |  2. Sign with client private key                   |
        |  3. Prepend clientId bytes (16 bytes)              |
        |  4. Encrypt with server public key (RSA)           |
        |                                                    |
        | -------- CoreWCF NetTcpBinding (port 45001) -----> |
        |          authenticate(encryptedData)               |
        |                                                    |
        |                5. Decrypt with server private key  |
        |                6. Extract clientId from prefix     |
        |                7. Load client public key by ID     |
        |                8. Verify signature                 |
        |                9. Create PushClientSession         |
        |                   - allocate sessionId (1-255)     |
        |                   - generate sessionAesKey (32B)   |
        |                10. Build VpnAuthResponse           |
        |                    {assignedIp, serverPublicIp,    |
        |                     sessionId, sessionAesKey}      |
        |                11. Sign + encrypt response         |
        |                                                    |
        | <------ encrypted VpnAuthResponse ----------------- |
        |                                                    |
        | 12. Decrypt + verify response                      |
        | 13. Store auth snapshot atomically                 |
        | 14. Start tunnel with assignedIp                   |
        | 15. Start UDP data plane                           |
        |                                                    |
```

**Key Types:**
- `VpnAuthRequest` (`Core\Primitives.fs:131-136`): `{clientId: VpnClientId, timestamp: DateTime, nonce: byte[]}`
- `VpnAuthResponse` (`Core\Primitives.fs:155-161`): `{assignedIp, serverPublicIp, sessionId, sessionAesKey}`
- `VpnPingRequest` (`Core\Primitives.fs:147-152`): `{clientId, sessionId, timestamp}`

**Encryption:**
- Auth messages use RSA encryption (hybrid: sign+encrypt) via `Softellect.Sys.Crypto`
- Client signs with its private key, encrypts with server's public key
- Server decrypts with its private key, verifies with client's public key

### Ping/Health Check Flow

```
Client (every 30s)          Server
     |                         |
     | pingSession(encrypted)  |
     | ----------------------> |
     |                         | Verify session still valid
     |                         | Update lastActivity
     | <-- Ok or Error ------- |
     |                         |
If Error: clear auth snapshot, re-authenticate
```

**Implementation:** `Client\Service.fs:167-182` (`pingSession`), `Server\Service.fs:73-80`

### UDP Data Plane Flow

```
+----------------+                                   +----------------+
|  VPN Client    |                                   |  VPN Server    |
|  (TUN adapter) |                                   |  (TUN adapter) |
+----------------+                                   +----------------+
        |                                                    |
        | [TUN] Capture IPv4 packet                          |
        |                                                    |
        | 1. Generate nonce (Guid)                           |
        | 2. Build plaintext payload:                        |
        |    [cmd=0x01] + [packet bytes]                     |
        | 3. Derive per-packet AES key:                      |
        |    HMAC-SHA256(sessionAesKey, nonce || 0x01)       |
        | 4. Encrypt payload with derived AES key            |
        | 5. Build datagram:                                 |
        |    [sessionId:1B + nonce:16B scrambled] + payload  |
        |                                                    |
        | ---------- UDP (same port as WCF) ---------------> |
        |                                                    |
        |                6. Parse datagram header            |
        |                7. Lookup session by sessionId      |
        |                8. Derive AES key from nonce        |
        |                9. Decrypt payload                  |
        |               10. Extract IPv4 packet              |
        |               11. Route packet:                    |
        |                   - VPN subnet -> client queue     |
        |                   - External -> NAT + send out     |
        |                                                    |
```

**Wire Format (per `Core\UdpProtocol.fs:14-23`):**
```
Byte offset  | Size | Content
-------------|------|------------------
0-16         | 17   | Header: sessionId (1B) + nonce (16B) scrambled together
17+          | var  | Encrypted payload
```

**Payload format (after decryption):**
```
Byte offset  | Size | Content
-------------|------|------------------
0            | 1    | Command byte (0x01=Data, 0x02=Keepalive, 0x03=Control)
1+           | var  | Command data (raw IPv4 packet for Data command)
```

**Constants (from `Core\UdpProtocol.fs`):**
- `PushMtu = 1550` (max datagram size)
- `MtuSize = 1300` (TUN MTU)
- `PushKeepaliveIntervalMs = 10000` (10 seconds)
- `PushSessionFreshnessSeconds = 60`

**Session Identification:**
- Server allocates `sessionId` (1 byte, values 1-255) per client session
- Session 0 is reserved for server
- UDP packets carry `sessionId` in header - NOT the full `clientId`
- Server looks up session by `sessionId` in `ClientRegistry`

---

## 3. Reuse Map: What Carries Over to Android

### Reusable As-Is

| Component | Location | Reason |
|-----------|----------|--------|
| UDP wire format | `Core\UdpProtocol.fs` | Protocol-agnostic byte layout; Kotlin can implement same packing/unpacking |
| AES key derivation | `Core\UdpProtocol.fs:106-120` | Standard HMAC-SHA256 + AES-256-CBC; any platform can implement |
| Message types | `Core\Primitives.fs` | `VpnAuthRequest`, `VpnAuthResponse`, `VpnPingRequest` - serialization is FsPickler, needs equivalent |
| Server UDP handling | `Server\UdpServer.fs` | No changes needed - accepts UDP from any client with valid sessionId |
| Server NAT/routing | `Server\Nat.fs`, `Server\PacketRouter.fs` | Works on raw IPv4 packets regardless of client platform |
| Server DNS proxy | `Server\DnsProxy.fs` | Protocol-level; Android client talks to VPN gateway IP |

### Reusable with Minor Adaptation

| Component | Location | Changes Needed |
|-----------|----------|----------------|
| Auth message format | `Core\Primitives.fs` | Serialize as JSON or length-prefixed binary instead of FsPickler |
| RSA crypto for auth | `Softellect.Sys.Crypto` | Kotlin/Java has equivalent RSA/signature APIs |
| Configuration loading | `Core\AppSettings.fs` | Android uses SharedPreferences or JSON config file |

### Not Reusable

| Component | Location | Reason |
|-----------|----------|--------|
| WinTun adapter | `Interop\WinTunAdapter.cs` | Windows-only; Android uses `VpnService` TUN fd |
| WFP Kill-switch | `Interop\KillSwitch.cs` | Windows Filtering Platform; Android uses VpnService routing |
| CoreWCF auth client | `Client\WcfClient.fs` | WCF is .NET-specific; need plain TCP or HTTP for Android |
| Windows Service hosting | `Client\Program.fs` | Android uses `Service` / `VpnService` |

### Critical Protocol Details for Android

1. **Session ID is the key**: UDP packets use 1-byte `sessionId` (not clientId) for routing. Server maps `sessionId` -> `PushClientSession`.

2. **Header scrambling**: The 17-byte header uses XOR-based position scrambling (`packByteAndGuid` / `unpackByteAndGuid` in `UdpProtocol.fs:124-175`). Android must implement identical scrambling.

3. **Per-packet AES key derivation**: Each packet uses a unique AES key derived from `sessionAesKey` and `nonce`:
   ```
   keyMaterial = HMAC-SHA256(sessionAesKey, nonce || 0x01)
   ivMaterial  = HMAC-SHA256(sessionAesKey, nonce || 0x02)[0..15]
   ```

4. **Encryption mode**: AES-256-CBC (from `tryEncryptAesKey` / `tryDecryptAesKey` in `Softellect.Sys.Crypto`).

5. **Server does NOT rely on stable client UDP source port**: Session is identified by `sessionId` in the packet header. Client can change IP/port (NAT traversal, network switch) and server will update `currentEndpoint`.

---

## 4. Proposed Android Design (VpnService + UDP Plane)

### Architecture Overview

```
+--------------------------------------------------+
|                  Android App                      |
+--------------------------------------------------+
|                                                   |
|  +---------------------------------------------+  |
|  |           VpnTunnelService                  |  |
|  |  (extends android.net.VpnService)           |  |
|  |                                             |  |
|  |  +---------------------------------------+  |  |
|  |  |  TUN fd (ParcelFileDescriptor)        |  |  |
|  |  +---------------------------------------+  |  |
|  |       ^                         |            |  |
|  |       | injectPacket()          | readPacket()|
|  |       |                         v            |  |
|  |  +----------------+  +------------------+   |  |
|  |  | InboundPump   |  | OutboundPump     |   |  |
|  |  | (Coroutine)   |  | (Coroutine)      |   |  |
|  |  +----------------+  +------------------+   |  |
|  |       ^                         |            |  |
|  |       |                         v            |  |
|  |  +---------------------------------------+  |  |
|  |  |         UDP Socket                    |  |  |
|  |  |  (DatagramSocket, protected)          |  |  |
|  |  +---------------------------------------+  |  |
|  |                     ^                        |  |
|  +---------------------|------------------------+  |
|                        |                          |
|  +---------------------|------------------------+  |
|  |     AuthManager     |                        |  |
|  |  (TCP auth/ping)    |                        |  |
|  |  - authenticate()   |                        |  |
|  |  - pingSession()    |                        |  |
|  |  - currentAuth: AtomicReference<AuthResponse>|
|  +---------------------------------------------+  |
|                                                   |
|  +---------------------------------------------+  |
|  |     ConnectivityWatcher                     |  |
|  |  - onNetworkChanged()                       |  |
|  |  - rebind UDP socket                        |  |
|  +---------------------------------------------+  |
+--------------------------------------------------+
```

### Thread/Coroutine Model

1. **VpnTunnelService** (Main Android Service)
   - Extends `VpnService`
   - Manages lifecycle, foreground notification
   - Creates TUN interface via `VpnService.Builder`

2. **InboundPump** (Coroutine on IO dispatcher)
   ```kotlin
   while (running) {
       val datagram = udpSocket.receive()
       val auth = authManager.currentAuth.get() ?: continue
       val packet = decryptAndParse(datagram, auth)
       if (packet != null) {
           tunOutputStream.write(packet)
       }
   }
   ```

3. **OutboundPump** (Coroutine on IO dispatcher)
   ```kotlin
   while (running) {
       val ipPacket = tunInputStream.read()
       val auth = authManager.currentAuth.get() ?: continue
       val datagram = buildAndEncrypt(ipPacket, auth)
       udpSocket.send(datagram)
   }
   ```

4. **KeepalivePump** (Coroutine)
   - Every 10 seconds: send keepalive datagram
   - Keeps NAT mapping alive

5. **AuthManager** (background thread or coroutine)
   - Holds `AtomicReference<VpnAuthResponse?>`
   - Supervisor loop: authenticate -> health check -> re-auth on failure
   - Uses TCP connection (not WCF) for auth/ping

### UDP Socket Lifecycle

```kotlin
class ProtectedUdpSocket(private val vpnService: VpnService) {
    private var socket: DatagramSocket? = null

    fun connect(serverIp: InetAddress, serverPort: Int) {
        socket?.close()
        socket = DatagramSocket().apply {
            // CRITICAL: Protect socket so packets don't loop through TUN
            vpnService.protect(this)
            connect(serverIp, serverPort)
        }
    }

    fun rebind() {
        // Called on network change
        val currentSocket = socket ?: return
        val serverIp = currentSocket.inetAddress
        val serverPort = currentSocket.port
        connect(serverIp, serverPort)
    }
}
```

### Network Switching Handling

```kotlin
class ConnectivityWatcher(
    private val context: Context,
    private val onNetworkChanged: () -> Unit
) {
    private val connectivityManager =
        context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager

    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            onNetworkChanged()
        }
        override fun onLost(network: Network) {
            onNetworkChanged()
        }
    }

    fun register() {
        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        connectivityManager.registerNetworkCallback(request, networkCallback)
    }
}
```

**Reconnect strategy:**
1. On network change callback:
   - UDP socket calls `rebind()` (close old, create new, protect, connect)
   - Auth snapshot remains valid (server identifies by sessionId, not source IP/port)
   - If ping fails within 30s, clear auth and re-authenticate

### MTU Strategy for Mobile

Recommendation: **MTU = 1280 bytes**

Rationale:
- `MtuSize = 1300` in current codebase is slightly too large for some mobile carriers
- IPv6 minimum MTU is 1280; many mobile networks have similar constraints
- Conservative approach avoids fragmentation on LTE/5G
- Can be made configurable in settings (1200-1400 range)

Set in VpnService.Builder:
```kotlin
builder.setMtu(1280)
```

### Keepalive and NAT Timeout Mitigation

| Setting | Value | Rationale |
|---------|-------|-----------|
| UDP Keepalive interval | 10 seconds | Current Windows client uses `PushKeepaliveIntervalMs = 10000` |
| TCP Ping interval | 30 seconds | Match `HealthCheckIntervalMs = 30000` |
| Socket receive timeout | 250ms | Allow checking auth/shutdown frequently |

Mobile NAT timeouts are typically 30-120 seconds for UDP. 10-second keepalives provide safety margin.

---

## 5. Proposed Android Auth/Ping Path (Server-Side TCP Endpoint)

### Recommendation: Length-Prefixed TCP Framing

**Why not CoreWCF:** Android has no WCF client. gRPC adds protobuf dependency. HTTP/REST requires web server setup.

**Proposed protocol:**

```
Message format:
+----------------+------------------+
| Length (4B LE) | Payload (JSON)   |
+----------------+------------------+
```

**Message types:**

```json
// AUTH_REQUEST (client -> server)
{
    "type": "AUTH",
    "clientId": "10e38c19-d220-4852-8589-82eca51ade92",
    "timestamp": "2025-12-27T10:30:00Z",
    "nonce": "base64-encoded-16-bytes",
    "signature": "base64-encoded-signature"
}

// AUTH_RESPONSE (server -> client)
{
    "type": "AUTH_RESPONSE",
    "success": true,
    "assignedIp": "10.66.77.5",
    "serverPublicIp": "10.66.77.1",
    "sessionId": 3,
    "sessionAesKey": "base64-encoded-32-bytes",
    "signature": "base64-encoded-signature"
}

// PING_REQUEST (client -> server)
{
    "type": "PING",
    "clientId": "10e38c19-d220-4852-8589-82eca51ade92",
    "sessionId": 3,
    "timestamp": "2025-12-27T10:31:00Z",
    "signature": "base64-encoded-signature"
}

// PING_RESPONSE (server -> client)
{
    "type": "PING_RESPONSE",
    "success": true,
    "signature": "base64-encoded-signature"
}

// ERROR_RESPONSE (server -> client)
{
    "type": "ERROR",
    "code": "SESSION_EXPIRED",
    "message": "Session not found or expired"
}
```

### Port Configuration

**Recommendation:** New TCP port = WCF port + 1

- Current WCF port: 45001 (from appsettings)
- Android TCP auth port: **45002**
- UDP data plane: **45001** (shared with WCF, different protocol)

Add to `appsettings.json`:
```json
{
  "appSettings": {
    "AndroidAuthPort": "45002"
  }
}
```

### Project Placement

**Recommendation:** Add to existing `Server` project (`Vpn\Server\`)

Rationale:
- Shares `ClientRegistry` with existing auth
- Shares key loading and crypto
- Single deployment unit
- No need for separate project for a simple TCP listener

**New file:** `Server\AndroidAuthServer.fs`

### Message Sequence

```
Android Client                              Windows Server
     |                                            |
     |  1. TCP connect to serverIp:45002          |
     | -----------------------------------------> |
     |                                            |
     |  2. AUTH_REQUEST (JSON, signed, encrypted) |
     | -----------------------------------------> |
     |                                            |
     |        3. Decrypt, verify signature        |
     |        4. Create PushClientSession         |
     |           (same ClientRegistry as WCF)     |
     |        5. Generate sessionAesKey           |
     |        6. Sign + encrypt AUTH_RESPONSE     |
     |                                            |
     | <----------------------------------------- |
     |  7. AUTH_RESPONSE                          |
     |                                            |
     |  8. TCP close (or keep for ping)           |
     |                                            |
     |  ... UDP data plane starts ...             |
     |                                            |
     |  9. Every 30s: TCP connect for ping        |
     | -----------------------------------------> |
     |                                            |
     |  10. PING_REQUEST (signed)                 |
     | -----------------------------------------> |
     |                                            |
     |        11. Verify session valid            |
     |        12. Update lastActivity             |
     |                                            |
     | <----------------------------------------- |
     |  13. PING_RESPONSE                         |
     |                                            |
```

### Integration with Existing Auth

The new TCP endpoint should:
1. Use the same `ClientRegistry` instance
2. Use the same key files (`ServerKeyPath`, `ClientKeysPath`)
3. Use the same `PushClientSession` structure
4. Call the same `createPushSession()` method

This ensures:
- Windows and Android clients share session pool (255 max)
- Same client can't have duplicate sessions
- No code duplication for session management

### Firewall/Port Exposure

Windows Firewall changes needed:
- Allow inbound TCP on port 45002 (Android auth)
- Existing rule for 45001 (WCF + UDP) remains

---

## 6. Manual Provisioning: Keys and Server Config

### Key Material Required on Android

| Item | Purpose | Size |
|------|---------|------|
| Client Private Key | Sign auth requests | RSA 2048+ bits |
| Client Public Key | Verify server responses | RSA 2048+ bits |
| Server Public Key | Encrypt auth requests, verify responses | RSA 2048+ bits |
| Client ID (GUID) | Identify client to server | 16 bytes |
| Server ID (GUID) | Identify server (for key lookup) | 16 bytes |

### Simple Approach: Config File in App-Private Storage

**File location:** `<app-internal-storage>/vpn_config.json`

```json
{
    "serverIp": "203.0.113.50",
    "serverWcfPort": 45001,
    "serverAuthPort": 45002,
    "clientId": "10e38c19-d220-4852-8589-82eca51ade92",
    "serverId": "fb22de75-91c0-4adc-8d17-87078ea226a4",
    "clientPrivateKey": "base64-encoded-pkcs8",
    "serverPublicKey": "base64-encoded-x509-spki",
    "useEncryption": true,
    "dnsServer": "10.66.77.1"
}
```

**Provisioning methods:**
1. **ADB push:** `adb push vpn_config.json /data/data/com.softellect.vpn/files/`
2. **File picker:** Settings screen allows importing from Downloads
3. **Manual entry:** Settings screen for server IP/port; key files picked separately

### Safer Approach: Android Keystore

**Architecture:**
1. Store long-term RSA private key in Android Keystore (hardware-backed if available)
2. Store encrypted config blob in SharedPreferences
3. Encrypt config blob with key stored in Keystore

**Key generation (on first setup):**
```kotlin
val keyGenerator = KeyPairGenerator.getInstance(
    KeyProperties.KEY_ALGORITHM_RSA, "AndroidKeyStore"
)
keyGenerator.initialize(
    KeyGenParameterSpec.Builder("vpn_client_key", PURPOSE_SIGN or PURPOSE_DECRYPT)
        .setDigests(KeyProperties.DIGEST_SHA256)
        .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1)
        .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_RSA_PKCS1)
        .build()
)
val keyPair = keyGenerator.generateKeyPair()
```

**Note:** This requires exporting the Android-generated public key to the server's `ClientKeysPath` folder.

### Key Rotation Process

1. Generate new key pair (Windows or Android)
2. Export public key to other party
3. Update client config with new private key
4. Test connection
5. (Optional) Remove old keys after successful test

Manual process is acceptable for friends & family VPN.

### Server Access Configuration Storage

**SharedPreferences (`vpn_settings`):**
```kotlin
val prefs = context.getSharedPreferences("vpn_settings", Context.MODE_PRIVATE)
prefs.edit()
    .putString("server_ip", "203.0.113.50")
    .putInt("server_auth_port", 45002)
    .putInt("server_udp_port", 45001)
    .putBoolean("use_encryption", true)
    .apply()
```

**Simple UI screen:**
- Server IP: EditText
- Auth Port: EditText (default 45002)
- UDP Port: EditText (default 45001)
- Use Encryption: Switch (default true)
- Import Keys: Button -> file picker

---

## 7. Risks / Unknowns Discovered in Code

### 1. FsPickler Serialization Format

**Risk:** Current WCF auth uses FsPickler for `VpnAuthRequest`/`VpnAuthResponse` serialization.

**Location:** `Core\ServiceInfo.fs`, `Client\WcfClient.fs:16-28`

**Impact:** Android cannot use FsPickler. The TCP auth endpoint must use a different serialization (JSON proposed).

**Mitigation:** Server TCP endpoint parses JSON; existing WCF path unchanged.

### 2. Signature/Encryption Algorithm Compatibility

**Risk:** `Softellect.Sys.Crypto` uses specific RSA parameters that must match on Android.

**Location:** `trySignAndEncrypt`, `tryDecryptAndVerify` in `Softellect.Sys.Crypto`

**Impact:** Need to verify exact algorithm (RSA-OAEP vs PKCS#1 v1.5, hash algorithm for signatures).

**Recommendation:** Document exact crypto parameters before Android implementation.

### 3. Session ID Exhaustion

**Risk:** Only 255 concurrent sessions (sessionId is 1 byte, 1-255).

**Location:** `Server\ClientRegistry.fs:93-114`

**Impact:** For friends & family use case, this is sufficient. If more clients needed, would require protocol change.

### 4. No Session Persistence

**Risk:** Server restart clears all sessions; clients must re-authenticate.

**Location:** `ClientRegistry` uses in-memory `ConcurrentDictionary`

**Impact:** Acceptable for this use case. Android client supervisor loop handles re-auth automatically.

### 5. Hardcoded Upstream DNS

**Risk:** DNS proxy uses hardcoded 1.1.1.1.

**Location:** `Server\DnsProxy.fs:16`

**Impact:** Minor; can be made configurable later.

### 6. Header Scrambling Algorithm

**Risk:** Custom XOR-based header scrambling is non-standard.

**Location:** `Core\UdpProtocol.fs:124-175` (`packByteAndGuid`, `unpackByteAndGuid`)

**Impact:** Must implement identical algorithm in Kotlin. Recommend adding test vectors to ensure compatibility.

### 7. Raw Socket Admin Rights

**Risk:** Server `ExternalGateway` uses raw sockets requiring admin privileges.

**Location:** `Server\ExternalInterface.fs:83`

**Impact:** Not relevant to Android client; server continues to require admin/elevated privileges.

---

## 8. Recommended Next Step (Stage 2 Implementation Plan)

### Phase 1: Server TCP Auth Endpoint

**Goal:** Add Android-compatible auth/ping endpoint to server.

**Files to create/modify:**
1. `Server\AndroidAuthServer.fs` - New TCP listener, JSON parsing, auth logic
2. `Core\AppSettings.fs` - Add `AndroidAuthPort` configuration
3. `Apps\Vpn\VpnServer\appsettings.json` - Add port setting

**Estimated scope:** ~300-400 lines of F# code

### Phase 2: Android Client Project Setup

**Goal:** Create minimal Android project with VpnService skeleton.

**Components:**
1. Kotlin project with Gradle
2. `VpnTunnelService` extending `VpnService`
3. `AuthManager` class for TCP auth
4. `UdpDataPlane` class for UDP handling
5. Settings screen for config

**Recommended stack:**
- Language: Kotlin
- Build: Gradle + Android SDK
- Async: Kotlin Coroutines
- Crypto: Android `Cipher`, `Signature`, `KeyStore`
- Networking: Standard `Socket`, `DatagramSocket`

### Phase 3: UDP Protocol Implementation

**Goal:** Implement UDP wire format matching Windows client.

**Key functions to port:**
- `packByteAndGuid` / `unpackByteAndGuid`
- `derivePacketAesKey`
- `buildPushDatagram` / `tryParsePushDatagram`
- `buildPayload` / `tryParsePayload`

**Recommendation:** Write test vectors from Windows implementation to verify Kotlin implementation.

### Phase 4: Integration Testing

**Goal:** End-to-end test with real server.

**Test plan:**
1. Android auth -> receive sessionId + AES key
2. Start VpnService, establish TUN
3. UDP keepalive works
4. Ping external IP (ICMP through VPN)
5. DNS resolution works
6. HTTP/HTTPS traffic works
7. Network switch (Wi-Fi -> LTE) recovery
8. Server restart recovery

---

## Appendix: Data Flow Diagram

```
WINDOWS CLIENT                          WINDOWS SERVER                    INTERNET
==============                          ==============                    ========

[WinTun Adapter]                        [WinTun Adapter]
      |                                       |
      v                                       v
[Tunnel.receiveLoop]                    [PacketRouter.receiveLoop]
      |                                       |
      | IPv4 packets                          | IPv4 packets
      v                                       v
[VpnPushUdpClient.sendLoop]             +---[DNS Query?]---> [DnsProxy] --> 1.1.1.1
      |                                 |         |
      | AES encrypt                     |         v
      | + header                        |   [Client queue]
      v                                 |
[UDP Socket] -------- UDP --------> [UDP Socket]
                                        |
                                        +---[VPN subnet?]---> [Client queue]
                                        |
                                        +---[External?]-----> [NAT.translateOutbound]
                                                                    |
                                                                    v
                                                              [ExternalGateway]
                                                                    |
                                                              Raw IP Socket
                                                                    |
                                                                    v
                                                              INTERNET HOSTS


RETURN PATH:

INTERNET                                WINDOWS SERVER
========                                ==============

[External hosts]                        [ExternalGateway.receiveLoop]
      |                                       |
      | Raw IPv4                              v
      +----------------------------> [IcmpProxy?] --> [Client queue]
      |                                       |
      +----------------------------> [NAT.translateInbound]
                                              |
                                              v
                                        [Client queue]
                                              |
                                              v
                                        [UdpServer.pushSendLoop]
                                              |
                                        AES encrypt
                                              |
                                              v
[VpnPushUdpClient.receiveLoop] <--- UDP --- [UDP Socket]
      |
      | AES decrypt
      v
[Tunnel.injectPacket]
      |
      v
[WinTun Adapter]
```

---

*End of Report*
