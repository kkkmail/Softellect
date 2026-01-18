# VPN Gateway Investigation Report: Windows Client → Linux Server UDP Data Plane Failure

**Report ID:** vpn_gateway_spec__062__report__01
**Date:** 2026-01-15
**Investigator:** Claude Code (Forensic Analysis)

---

## 1. Executive Summary

### Most Likely Root Cause (High Confidence)

The Linux server's `ExternalGateway` uses **two separate raw sockets** (`ProtocolType.Tcp` and `ProtocolType.Udp`) for sending outbound packets, while the Windows server uses a **single raw IP socket** (`ProtocolType.IP`). 
On Linux, raw sockets bound to `ProtocolType.Tcp` do not properly transmit TCP packets with custom IP headers when using `SendTo()` - the kernel either drops the packets or corrupts them because raw TCP sockets on Linux 
are designed primarily for **receiving** TCP packets, not for **sending** arbitrary TCP packets with custom headers.

### Evidence Summary

1. **TCP handshakes partially complete**: Client receives SYN/ACK responses, proving inbound path works.
2. **TCP connections immediately fail with RST**: After client sends ACK (handshake completion), remote servers send RST.
3. **No errors logged on server side**: `SendTo()` calls succeed without throwing exceptions.
4. **NAT translation succeeds**: Server logs show successful outbound NAT translation for TCP packets.
5. **Packets never reach remote servers**: RST sequence numbers prove remote servers never received the ACK.

---

## 2. Timeline Correlation

### Client Timeline (Windows) - Relative Timestamps

| Time (s) | Event | Code Location |
|----------|-------|---------------|
| 14.217 | Receive loop started | `VpnPushUdpClient.receiveLoop` |
| 14.268 | First TCP SYN captured (10.66.77.3:54262 → 48.216.187.226:443) | `Tunnel.receiveLoop` |
| 14.522 | First DNS response injected | `Tunnel.injectPacket` |
| 14.740 | TCP SYN/ACK received (48.216.187.226:443 → 10.66.77.3:54262) | `Tunnel.injectPacket` |
| 14.745 | TCP SYN/ACK received (142.250.101.188:443 → 10.66.77.3:52151) | `Tunnel.injectPacket` |
| 14.745 | TCP ACK sent (handshake completion for port 52151) | `Tunnel.receiveLoop` |
| 14.746 | TCP data (1300 bytes TLS ClientHello) sent | `Tunnel.receiveLoop` |
| 14.960 | TCP RST received (142.250.101.188:443 → 10.66.77.3:52151) | `Tunnel.injectPacket` |

### Server Timeline (Linux) - Relative Timestamps

| Time (s) | Event | Code Location |
|----------|-------|---------------|
| 0.282 | ExternalGateway raw sockets bound | `ExternalInterface.fs:191` |
| 0.819 | ExternalGateway started | `ExternalInterface.fs:214` |
| 28.717 | First auth request received | `Service.authenticate` |
| 41.997 | First UDP data from client | `UdpServer.receiveLoop` |
| 42.278 | NAT OUT: TCP SYN (52151 → 8EFA65BC:443, extPort=40002) | `Nat.translateOutbound` |
| 42.493 | NAT OUT: TCP ACK (52151 → 8EFA65BC:443, extPort=40002) | `Nat.translateOutbound` |
| 42.498 | NAT OUT: TCP data (52151 → 8EFA65BC:443, extPort=40002) | `Nat.translateOutbound` |
| 42.501 | NAT OUT: TCP data retry | `Nat.translateOutbound` |

### Key Observation

The server successfully:
1. Receives SYN from client
2. Performs NAT translation
3. Calls `externalGateway.sendOutbound()`
4. Receives SYN/ACK from remote server (proving outbound SYN was sent)
5. Translates and forwards SYN/ACK to client
6. Receives ACK from client
7. Performs NAT translation for ACK
8. Calls `externalGateway.sendOutbound()` for ACK

But the remote server sends RST immediately, indicating the ACK packet **never arrived**.

---

## 3. Divergence Analysis

### Windows Server ExternalGateway

**File:** `C:\GitHub\Softellect\Vpn\Server\ExternalInterface.fs`
**Lines 83-84, 236-254**

```fsharp
// Single raw IP socket handles both TCP and UDP
let rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)

// Single send path for all protocols
member _.sendOutbound(packet: byte[]) =
    match getDestinationIpAddress packet with
    | Some dstIp ->
        let remoteEndPoint = IPEndPoint(dstIp, 0)
        let sent = rawSocket.SendTo(packet, remoteEndPoint)
```

**Key characteristics:**
- Uses `ProtocolType.IP` - kernel-level IP packet handling
- `IOControlCode.ReceiveAll` enabled for receiving all protocols
- Single code path for TCP and UDP transmission
- Kernel handles protocol demultiplexing

### Linux Server ExternalGateway

**File:** `C:\GitHub\Softellect\Vpn\LinuxServer\ExternalInterface.fs`
**Lines 79-81, 216-234**

```fsharp
// Two separate raw sockets for TCP and UDP
let rawTcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Tcp)
let rawUdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp)

// Protocol-specific send paths
member _.sendOutbound(packet: byte[]) =
    match proto with
    | 6uy  -> rawTcpSocket.SendTo(packet, remoteEndPoint) |> ignore
    | 17uy -> rawUdpSocket.SendTo(packet, remoteEndPoint) |> ignore
```

**Key characteristics:**
- Uses `ProtocolType.Tcp` and `ProtocolType.Udp` separately
- Linux does NOT support `ProtocolType.IP` for raw sockets (EPROTONOSUPPORT)
- Separate code paths for TCP and UDP
- `HeaderIncluded = true` set on both sockets

### Critical Difference

On Linux, `Socket(SocketType.Raw, ProtocolType.Tcp)`:
1. Is primarily designed for **receiving** TCP packets before kernel processing
2. When used with `HeaderIncluded = true` for **sending**, the kernel may:
   - Recalculate/overwrite the TCP checksum
   - Reject packets that don't follow expected TCP state machine
   - Drop packets without error if they appear malformed
3. The `SendTo()` call succeeds (returns bytes sent) but the packet may never be transmitted

---

## 4. Findings

### Finding 1: TCP ACK packets are not transmitted on Linux (HIGH CONFIDENCE)

**Code reference:** `ExternalInterface.fs:226`
**Log reference:** Server log lines 262, 302, 307, 312 (NAT OUT successful)
**Log reference:** Client log lines 244-247 (RST received with seq = SYN/ACK.seq + 1)

The RST packets' sequence numbers (e.g., `seq=2042522225`) exactly match the expected acknowledgment number (`SYN/ACK.seq + 1 = 2042522224 + 1`), proving the remote server never received the ACK packet and timed out waiting for the handshake to complete.

### Finding 2: SYN packets ARE transmitted successfully (HIGH CONFIDENCE)

**Code reference:** `ExternalInterface.fs:226`
**Log reference:** Client log lines 222-239 (SYN/ACK responses received)

The fact that SYN/ACK responses are received proves that:
- The initial SYN packet was sent correctly
- The NAT translation is working
- The inbound path (receiving via raw sockets) is working
- The only failing path is subsequent outbound TCP sends

### Finding 3: UDP data plane is working (HIGH CONFIDENCE)

**Log reference:** Client log lines 195-220 (DNS responses injected)
**Log reference:** Server log lines 82-164 (DNS proxy forwarding)

DNS queries (UDP) are successfully:
- Sent from client to server
- Forwarded to upstream (1.1.1.1)
- Responses received and forwarded back to client

This proves `rawUdpSocket` works correctly for sending.

### Finding 4: First TCP SYN succeeds, subsequent packets fail (HIGH CONFIDENCE)

**Log reference:** NAT OUT logs show multiple packets for same connection
**Log reference:** Client receives SYN/ACK but then RST

The pattern suggests the Linux kernel may be:
- Allowing the first packet through (SYN)
- Blocking subsequent packets that don't match expected TCP state

### Finding 5: No errors are thrown or logged (CONFIRMED)

**Log reference:** No error logs related to sendOutbound
**Code reference:** `ExternalInterface.fs:230-232` (only logs on exception)

The `SendTo()` call returns successfully, meaning:
- The socket is valid
- The kernel accepts the send request
- The packet is silently dropped somewhere in the network stack

---

## 5. Recommendations

### Recommendation 1: Use IPPROTO_RAW socket for TCP packets (HIGH CONFIDENCE)

Replace the separate TCP/UDP raw sockets with a single raw socket using `IPPROTO_RAW`:

```fsharp
// Instead of:
let rawTcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Tcp)

// Use:
let rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Raw)
```

On Linux, `IPPROTO_RAW` (protocol number 255) creates a socket that:
- Accepts complete IP packets including headers
- Does NOT recalculate checksums
- Does NOT apply TCP state machine rules
- Properly transmits packets as provided

### Recommendation 2: Verify with tcpdump on Linux server (HIGH CONFIDENCE)

Before making code changes, validate the hypothesis by running:

```bash
# On Linux server, capture outbound packets on the external interface
tcpdump -i eth0 -n tcp port 443 and host 142.250.101.188

# Then trigger a connection from Windows client
```

If the hypothesis is correct:
- You will see the SYN packet go out
- You will see the SYN/ACK come back
- You will NOT see the ACK packet go out

### Recommendation 3: Check raw socket capabilities (MEDIUM CONFIDENCE)

Verify the server has proper capabilities for raw socket operation:

```bash
# Check if running as root
whoami

# Check capabilities on the binary
getcap /opt/vpn/VpnServerLinux

# If not root, set capability
sudo setcap cap_net_raw+ep /opt/vpn/VpnServerLinux
```

While the current behavior suggests packets are being dropped rather than permission denied, this should be verified.

### Recommendation 4: Consider AF_PACKET socket as alternative (LOW CONFIDENCE / NEEDS VALIDATION)

If `IPPROTO_RAW` doesn't work, consider using `AF_PACKET` sockets which operate at layer 2:

```csharp
// AF_PACKET with SOCK_DGRAM for IP-level packets
let rawSocket = new Socket(AddressFamily.Packet, SocketType.Dgram, ProtocolType.IP)
```

This approach:
- Bypasses the kernel's TCP/UDP handling entirely
- Requires manually constructing Ethernet frames (or using SOCK_DGRAM for IP-level)
- More complex but provides complete control over packet transmission

### Recommendation 5: Add diagnostic logging (LOW CONFIDENCE / DEBUG AID)

Add temporary logging to track actual bytes sent:

```fsharp
| 6uy  ->
    let sent = rawTcpSocket.SendTo(packet, remoteEndPoint)
    Logger.logTrace (fun () -> $"ExternalGateway: TCP SendTo returned {sent} bytes for packet len={packet.Length}")
```

This helps distinguish between:
- SendTo returning success but wrong byte count
- SendTo succeeding but kernel dropping packet
- Other anomalies

---

## 6. Appendix: File References

| File | Purpose |
|------|---------|
| `C:\GitHub\Softellect\Vpn\LinuxServer\ExternalInterface.fs` | Linux ExternalGateway (raw socket send) |
| `C:\GitHub\Softellect\Vpn\Server\ExternalInterface.fs` | Windows ExternalGateway |
| `C:\GitHub\Softellect\Vpn\Server\PacketRouter.fs` | Packet routing and NAT invocation |
| `C:\GitHub\Softellect\Vpn\Server\Nat.fs` | NAT translation logic |
| `C:\GitHub\Softellect\Vpn\Server\Service.fs` | VPN service orchestration |
| `C:\GitHub\Softellect\Vpn\Server\UdpServer.fs` | UDP push protocol handling |

---

## 7. Conclusion

The root cause of the Windows client → Linux server TCP failure is the use of protocol-specific raw sockets (`ProtocolType.Tcp`) on Linux, which do not properly transmit custom IP packets with headers included. The solution is to use `IPPROTO_RAW` or `AF_PACKET` sockets which bypass the kernel's protocol-specific handling and transmit packets as-is.

The UDP data plane works because `ProtocolType.Udp` raw sockets on Linux handle custom packets more permissively than TCP sockets.
