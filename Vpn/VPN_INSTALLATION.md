# VPN Service Installation Guide

This document describes how to install and configure the Softellect VPN Client and Server services on Windows.

## Prerequisites

- Windows 10/11 or Windows Server 2016+
- .NET 9.0 Runtime
- Administrator privileges
- WinTun driver (for virtual network adapter)

## VPN Client Installation

### 1. Build the Solution

Build the VPN solution in Release mode:

```powershell
dotnet build --configuration Release
```

### 2. Configure appsettings.json

Edit `VpnClient\bin\x64\Release\net9.0\appsettings.json`:

```json
{
  "appSettings": {
    "ProjectName": "VpnClient",
    "VpnClientAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=<SERVER_IP>;netTcpServicePort=45080;netTcpServiceName=VpnService;netTcpSecurityMode=NoSecurity",
    "VpnClientId": "<YOUR_CLIENT_GUID>",
    "VpnServerId": "<SERVER_GUID>",
    "ClientKeyPath": "C:\\Keys\\VpnClient",
    "ServerPublicKeyPath": "C:\\Keys\\VpnServer",
    "LocalLanExclusions": "192.168.0.0/16;10.0.0.0/8;172.16.0.0/12;169.254.0.0/16;127.0.0.0/8"
  }
}
```

**Configuration Parameters:**
- `VpnClientId` - Unique GUID identifying this client (must match the key file name)
- `VpnServerId` - GUID of the VPN server (must match the server's public key file name)
- `ClientKeyPath` - Folder containing client private key (`.key`) and public key (`.pkx`)
- `ServerPublicKeyPath` - Folder containing the server's public key (`.pkx`)
- `LocalLanExclusions` - Semicolon-separated list of CIDR subnets to exclude from VPN (allow local LAN access)

### 3. Generate Keys

Use ClientAdm to generate client keys:

```powershell
cd ClientAdm\bin\x64\Release\net9.0
.\ClientAdm.exe --generate-keys
```

This creates:
- `C:\Keys\VpnClient\<VpnClientId>.key` - Client private key
- `C:\Keys\VpnClient\<VpnClientId>.pkx` - Client public key

Copy the server's public key to `C:\Keys\VpnServer\<VpnServerId>.pkx`.

### 4. Install the Service

Run PowerShell as Administrator:

```powershell
cd VpnClient\bin\x64\Release\net9.0
.\Install-VpnClient.ps1
```

The installation script will:
1. Grant WFP (Windows Filtering Platform) permissions to LOCAL SERVICE
2. Install the VpnClient Windows service
3. Start the service automatically

### 5. Service Management

**Start the service:**
```powershell
.\Start-VpnClient.ps1
```

**Stop the service:**
```powershell
.\Stop-VpnClient.ps1
```

**Uninstall the service:**
```powershell
.\Uninstall-VpnClient.ps1
```

**Reinstall the service:**
```powershell
.\Reinstall-VpnClient.ps1
```

## VPN Server Installation

### 1. Configure appsettings.json

Edit `VpnServer\bin\x64\Release\net9.0\appsettings.json`:

```json
{
  "appSettings": {
    "ProjectName": "VpnServer",
    "VpnServerAccessInfo": "NetTcpServiceInfo|netTcpServiceAddress=0.0.0.0;netTcpServicePort=45080;netTcpServiceName=VpnService;netTcpSecurityMode=NoSecurity",
    "VpnServerId": "<SERVER_GUID>",
    "ServerKeyPath": "C:\\Keys\\VpnServer",
    "ClientPublicKeyPath": "C:\\Keys\\VpnClients"
  }
}
```

### 2. Generate Server Keys

Use ServerAdm to generate server keys:

```powershell
cd ServerAdm\bin\x64\Release\net9.0
.\ServerAdm.exe --generate-keys
```

### 3. Install the Service

```powershell
cd VpnServer\bin\x64\Release\net9.0
.\Install-VpnServer.ps1
```

## Windows Filtering Platform (WFP) Permissions

The VPN Client uses WFP for the kill-switch functionality. When running as a Windows service under `NT AUTHORITY\LOCAL SERVICE`, special permissions are required.

### Automatic Permission Grant

The `Install-VpnClient.ps1` script automatically calls `Grant-WfpPermissions` which:
- Grants LOCAL SERVICE access to the Base Filtering Engine (BFE) service
- Configures required registry permissions
- Grants SeImpersonatePrivilege and SeSecurityPrivilege

### Manual Permission Grant

If you need to grant WFP permissions manually:

```powershell
cd VpnClient\bin\x64\Release\net9.0
. .\Grant-WfpPermissions.ps1
Grant-WfpPermissions
```

### After Granting Permissions

You may need to restart the BFE service or reboot for changes to take effect:

```powershell
Restart-Service -Name BFE -Force
```

## Troubleshooting

### Service Fails to Start

1. Check the log files in `C:\Logs\VpnClient\` or `C:\Logs\VpnServer\`
2. Verify key files exist and have correct GUIDs
3. Ensure WFP permissions are granted (for client)

### WFP Error 0x00000005 (ACCESS_DENIED)

This indicates the service account lacks WFP permissions:
1. Run `Grant-WfpPermissions` as Administrator
2. Restart the BFE service: `Restart-Service -Name BFE -Force`
3. Restart the VPN Client service

### WFP Error 0x80320024 (FWP_E_INVALID_ACTION_TYPE)

This indicates incorrect action type values. Ensure `FWP_ACTION_BLOCK` and `FWP_ACTION_PERMIT` include the `FWP_ACTION_FLAG_TERMINATING` flag (0x1001 and 0x1002 respectively). See `WFP_IMPLEMENTATION.md` for details.

### Key File Not Found

Ensure:
- Key files exist in the configured paths
- File names match the GUIDs in appsettings.json
- Client has `.key` and `.pkx` files
- Server public key is copied to client's `ServerPublicKeyPath`

### Connection Issues

1. Verify server IP and port in `VpnClientAccessInfo`
2. Check firewall rules on the server
3. Ensure WinTun driver is installed
4. Check that the server is running and accessible

## Log Files

Logs are stored in:
- Client: `C:\Logs\VpnClient\YYYY-MM-DD.log`
- Server: `C:\Logs\VpnServer\YYYY-MM-DD.log`

## Service Account

Both services run under `NT AUTHORITY\LOCAL SERVICE` by default. This account:
- Has limited privileges (more secure than LOCAL SYSTEM)
- Requires explicit WFP permissions for kill-switch functionality
- Can access network resources as anonymous

To change the service account, modify the `$Login` parameter in the installation scripts or use Services Management Console (services.msc).
