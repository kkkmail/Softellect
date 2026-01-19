# Linux VPN Server – Configuration Summary

This document consolidates **all Linux-side configuration** performed during debugging and deployment of the VPN server.

All occurrences of the VPN port use the placeholder:

```
<vpn_server_port>
```

---

## 1. Firewall (firewalld)

Open TCP and UDP ports persistently:

```bash
firewall-cmd --permanent --add-port=<vpn_server_port>/tcp
firewall-cmd --permanent --add-port=<vpn_server_port>/udp
firewall-cmd --reload

# Verify
firewall-cmd --list-ports | grep <vpn_server_port>
```

---

## 2. Firewall stack verification

```bash
systemctl status firewalld
nft list ruleset
iptables -L -n
```

---

## 3. .NET 10 installation (manual)

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet
ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
```

---

## 4. ICU / globalization workaround

```bash
cat >/etc/profile.d/dotnet-invariant.sh <<'EOF'
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EOF

source /etc/profile.d/dotnet-invariant.sh

# Verify
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet --info
```

---

## 5. VPN runtime directory

```bash
mkdir -p /opt/vpn
# (VPN binaries uploaded here via WinSCP / mc)
```

---

## 6. Run VPN server (foreground)

```bash
ASPNETCORE_URLS=http://0.0.0.0:<vpn_server_port> dotnet VpnServerLinux.dll
```

---

## 7. Verify listening sockets

```bash
ss -lntup | grep :<vpn_server_port>
ss -lunp  | grep :<vpn_server_port>
```

---

## 8. Network / interface verification

```bash
ip link show SoftellectVPN
ip addr show SoftellectVPN
ip route show
```

---

## 9. TCP RST suppression (debugging)

```bash
iptables -A OUTPUT -p tcp --sport 40000:65535 --tcp-flags RST RST -j DROP
```

---

## 10. NIC offload disabling (debugging)

```bash
# ---- receive-side offloads ----
ethtool -K eth0 gro off
ethtool -K eth0 rx-gro-hw off
ethtool -K eth0 lro off

# ---- transmit-side segmentation ----
ethtool -K eth0 tso off
ethtool -K eth0 gso off

# ---- checksum offloads ----
ethtool -K eth0 tx off
ethtool -K eth0 rx off

# ---- VLAN offloads ----
ethtool -K eth0 rxvlan off
ethtool -K eth0 txvlan off

# ---- UDP / tunnel offloads ----
ethtool -K eth0 tx-udp-segmentation off
ethtool -K eth0 tx-udp_tnl-segmentation off
ethtool -K eth0 tx-udp_tnl-csum-segmentation off

# ---- verify effective state ----
ethtool -k eth0 | egrep 'gro|gso|lro|tso|udp|rx-gro'
```

---

## 11. Packet capture (numeric ports only)

```bash
tcpdump -ni eth0 -nn udp port <vpn_server_port> > tcpdump.txt 2>&1
```

---

## 12. Net-tools installation (netstat)

```bash
dnf -y install net-tools

# UDP statistics
netstat -su
```

---

## 13. Time / timezone verification

```bash
timedatectl
date
date -u
```

---

## 14. Privileges / capabilities verification

```bash
id
capsh --print | grep cap_net
```
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
