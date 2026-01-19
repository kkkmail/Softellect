## 15. Run VPN server as a systemd service

This installs the VPN server as a **background service** that:
- starts automatically on reboot
- runs as `root` (required for TUN)
- switches logging to **service mode (log4net)**

### 15.1. Enable scripts (one-time)

From `/opt/vpn`:

```bash
chmod +x *.sh
```

This enables all VPN helper scripts at once.

---

### 15.2. Install and start the service

From `/opt/vpn`:

```bash
./Install-VpnServer.sh
```

Expected result:
- service `vpnserverlinux.service` is installed
- service is started
- control returns immediately to the terminal

Verify:
```bash
systemctl status vpnserverlinux.service
```

---

### 15.3. Stop and uninstall the service

From `/opt/vpn`:

```bash
./Uninstall-VpnServer.sh
```

This:
- stops the service
- disables autostart
- removes the systemd unit

---

### 15.4. Service logs (systemd / journald)

Follow live logs:
```bash
./Tail-VpnServerLog.sh
```

Export logs to a local file:
```bash
./Get-VpnServerLog.sh
```

Default output file name:
```
vpn__YYYYMMDD__NNN.log
```
(created in the current folder)

Clear log view (sets a new “since” marker):
```bash
./Clear-VpnServerLog.sh
```

---

### 15.5. Logging mode behavior (important)

- **Foreground run**:
  ```bash
  ASPNETCORE_URLS=http://0.0.0.0:<vpn_server_port> dotnet VpnServerLinux.dll > vpn.log 2>&1
  ```
  → console logging (unchanged behavior)

- **systemd service run**:
  ```bash
  ./Install-VpnServer.sh
  ```
  → service mode logging (log4net), triggered via:
  ```
  RUNNING_AS_SERVICE=1
  ```
