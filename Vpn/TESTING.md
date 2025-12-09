# VPN Testing Guide

This guide explains how to test the Softellect VPN on a local network with two machines.

## Prerequisites

### Both Machines
- Windows 10/11 (64-bit)
- .NET 9 Runtime installed
- Administrator privileges
- Firewall configured to allow the VPN port (default: 5080)

### Server Machine
- Static IP address or known hostname on the local network

### Client Machine
- Network connectivity to the server

> **Note**: The WinTun driver (`wintun.dll`) is bundled with the build and automatically copied to the output directories. No manual driver installation is required.

## Step 1: Configure the Server

### 1.1 Create Key Storage Directories

On the **server machine**, create directories for keys:

```cmd
mkdir C:\Keys\VpnServer
mkdir C:\Keys\VpnClients
```

### 1.2 Configure Server Settings

Edit `C:\GitHub\Softellect\Vpn\VpnServer\appsettings.json`:

> **Note**: The same appsettings.json is shared between VpnServer and VpnServerAdm (via project link).

```json
{
  "appSettings": {
    "ProjectName": "VpnServerAdm",
    "VpnServerAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=0.0.0.0;netTcpServicePort=5080;netTcpServiceName=VpnService;netTcpSecurityMode=NoSecurity",
    "VpnSubnet": "10.66.77.0/24",
    "ServerKeyPath": "C:\\Keys\\VpnServer",
    "ClientKeysPath": "C:\\Keys\\VpnClients"
  }
}
```

Replace `0.0.0.0` with the server's actual LAN IP address (e.g., `192.168.1.100`).

### 1.3 Generate Server Keys

```cmd
cd C:\GitHub\Softellect\Vpn\ServerAdm\bin\x64\Debug\net9.0
VpnServerAdm.exe GenerateKeys
```

Expected output:
```
Generated server keys at C:\Keys\VpnServer
Key ID: <some-guid>
Keys generated successfully.
```

### 1.4 Export Server Public Key

Export the public key to share with the client:

```cmd
VpnServerAdm.exe ExportPublicKey -ofn C:\Keys\Export
```

This creates a `.pkx` file that needs to be copied to the client machine.

## Step 2: Configure the Client

### 2.1 Create Key Storage Directories

On the **client machine**, create directories:

```cmd
mkdir C:\Keys\VpnClient
```

### 2.2 Configure Client Settings

Edit `C:\GitHub\Softellect\Vpn\VpnClient\appsettings.json`:

> **Note**: The same appsettings.json is shared between VpnClient and VpnClientAdm (via project link).

```json
{
  "appSettings": {
    "ProjectName": "VpnClientAdm",
    "VpnClientAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=SERVER_IP;netTcpServicePort=5080;netTcpServiceName=VpnService;netTcpSecurityMode=NoSecurity",
    "VpnClientId": "{00000000-0000-0000-0000-000000000001}",
    "ClientKeyPath": "C:\\Keys\\VpnClient",
    "ServerPublicKeyPath": "C:\\Keys\\VpnServerKey",
    "LocalLanExclusions": "192.168.0.0/16;10.0.0.0/8;172.16.0.0/12;169.254.0.0/16;127.0.0.0/8"
  }
}
```

Replace `SERVER_IP` with the server's LAN IP address.

**Important**: Generate a unique GUID for `VpnClientId` or use the one shown above for testing.

### 2.3 Generate Client Keys

```cmd
cd C:\GitHub\Softellect\Vpn\ClientAdm\bin\x64\Debug\net9.0
VpnClientAdm.exe GenerateKeys
```

### 2.4 Export Client Public Key

```cmd
VpnClientAdm.exe ExportPublicKey -ofn C:\Keys\Export
```

### 2.5 Import Server Public Key

Copy the server's `.pkx` file from Step 1.4 to the client machine, then:

```cmd
VpnClientAdm.exe ImportServerKey -ifn C:\Keys\ServerPublicKey.pkx
```

## Step 3: Register Client on Server

### 3.1 Copy Client Public Key to Server

Copy the client's `.pkx` file (from Step 2.4) to the server machine.

### 3.2 Import Client Key on Server

On the **server machine**:

```cmd
cd C:\GitHub\Softellect\Vpn\ServerAdm\bin\x64\Debug\net9.0
VpnServerAdm.exe ImportClientKey -ifn C:\Path\To\ClientPublicKey.pkx
```

### 3.3 Register the Client

```cmd
VpnServerAdm.exe RegisterClient -id {00000000-0000-0000-0000-000000000001} -n "TestClient" -ip 10.66.77.2
```

## Step 4: Start the VPN Server

On the **server machine**, open an **Administrator** command prompt:

```cmd
cd C:\GitHub\Softellect\Vpn\VpnServer\bin\x64\Debug\net9.0
VpnServer.exe
```

You should see:
```
Starting VPN Service...
Packet router started with IP: 10.66.77.1
VPN Service started successfully
```

## Step 5: Start the VPN Client

On the **client machine**, open an **Administrator** command prompt:

```cmd
cd C:\GitHub\Softellect\Vpn\VpnClient\bin\x64\Debug\net9.0
VpnClient.exe
```

You should see:
```
Starting VPN Client Service...
Enabling kill-switch...
Kill-switch enabled
Authenticating with server...
Authenticated successfully. Assigned IP: 10.66.77.2
Tunnel started with IP: 10.66.77.2
VPN Client connected with IP: 10.66.77.2
```

## Step 6: Verify VPN Connection

### 6.1 Check Network Adapters

On the **client machine**, open a command prompt and run:

```cmd
ipconfig /all
```

Look for the `SoftellectVPN` adapter with IP `10.66.77.2`.

### 6.2 Ping the VPN Server

From the client:

```cmd
ping 10.66.77.1
```

This should succeed if the tunnel is working.

### 6.3 Check Route Table

```cmd
route print
```

Look for routes through the `10.66.77.x` network.

## Step 7: Verify Traffic Goes Through VPN

### Method 1: Traceroute

On the client, trace a route to an external IP:

```cmd
tracert 8.8.8.8
```

The first hop should be `10.66.77.1` (the VPN server).

### Method 2: Wireshark on Server

1. Install Wireshark on the server
2. Capture on the `SoftellectVPN` adapter
3. From the client, browse the internet or ping external addresses
4. You should see the traffic in Wireshark with source IP `10.66.77.2`

### Method 3: Check External IP

On the client, before and after connecting to VPN:

```cmd
curl ifconfig.me
```

Or visit https://whatismyip.com

- Before VPN: Shows client's real public IP
- After VPN: Should show server's public IP (if server has NAT configured)

### Method 4: Network Monitor

On the client, use Resource Monitor:

1. Open Resource Monitor (resmon.exe)
2. Go to Network tab
3. Watch "Network Activity" while browsing
4. Traffic should flow through the SoftellectVPN adapter

## Step 8: Test Kill-Switch

The kill-switch ensures all traffic is blocked if VPN disconnects unexpectedly.

### Test Procedure

1. Connect the VPN client (Step 5)
2. Verify internet access works
3. **On the server**, stop VpnServer.exe (Ctrl+C)
4. **On the client**, try to access the internet:
   ```cmd
   ping 8.8.8.8
   ```
5. The ping should **fail** (kill-switch blocking traffic)
6. Local LAN access should still work:
   ```cmd
   ping 192.168.1.1
   ```
   This should succeed (local LAN excluded from kill-switch)

## Troubleshooting

### "Failed to create adapter"
- Run as Administrator
- Verify `wintun.dll` is in the same folder as the executable (should be there by default)
- Check that no other application is using a WinTun adapter with the same name

### "Failed to enable kill-switch"
- Run as Administrator
- Check Windows Filtering Platform service is running:
  ```cmd
  sc query BFE
  ```
- Disable other VPN software that might conflict

### "Authentication failed"
- Verify client ID matches on both sides
- Check server's ClientKeysPath contains the client's public key
- Verify network connectivity (can you ping the server?)

### "Connection timeout"
- Check firewall allows port 5080 (TCP)
- Verify server IP address in client config
- Check Windows Firewall on both machines:
  ```cmd
  netsh advfirewall firewall add rule name="VPN Server" dir=in action=allow protocol=tcp localport=5080
  ```

### Server not receiving packets
- Check Windows Firewall
- Verify IP forwarding is enabled on server:
  ```cmd
  netsh interface ipv4 show interface
  ```
- Enable IP forwarding if needed:
  ```cmd
  netsh interface ipv4 set interface "SoftellectVPN" forwarding=enabled
  ```

## Installing as Windows Services

For production use, install as Windows services:

### Server
```cmd
sc create VpnServer binPath= "C:\Path\To\VpnServer.exe" start= auto
sc start VpnServer
```

### Client
```cmd
sc create VpnClient binPath= "C:\Path\To\VpnClient.exe" start= auto
sc start VpnClient
```

## Network Diagram

```
Client Machine                          Server Machine
+------------------+                    +------------------+
|                  |                    |                  |
| Real IP:         |                    | Real IP:         |
| 192.168.1.50     |                    | 192.168.1.100    |
|                  |                    |                  |
| VPN IP:          |   WCF (TCP:5080)   | VPN IP:          |
| 10.66.77.2       |<==================>| 10.66.77.1       |
|                  |                    |                  |
| SoftellectVPN    |                    | SoftellectVPN    |
| Adapter          |                    | Adapter          |
+------------------+                    +------------------+
        |                                       |
        |            VPN Tunnel                 |
        +---------------------------------------+
                         |
                    Internet
                  (via Server)
```

## Security Notes

1. **NoSecurity mode**: The current configuration uses `netTcpSecurityMode=NoSecurity` for testing. For production, configure transport security.

2. **Kill-switch**: The client's kill-switch blocks ALL non-local traffic when enabled, even if VPN connection fails. This is by design for privacy.

3. **Key management**: Keep private keys secure. Only share `.pkx` (public key) files.

4. **Firewall**: Only open port 5080 to trusted networks.
