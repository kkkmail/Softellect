# vpn_gateway_spec__013__fix_client_tun_routing_and_dns.md

## Goal

When the VPN client enables the kill-switch, it must still be able to reach the internet **through the VPN tunnel**. Right now the WinTun adapter gets an IP, but Windows is not told to route default traffic (and DNS) via that adapter, so the kill-switch blocks the normal NIC and the client loses connectivity.

Fix: after the tunnel is up and the adapter IP is assigned, configure:
1) DNS on the WinTun adapter  
2) Default routing through the VPN gateway (server VPN IP)

## Scope / Constraints

- Work in: `C:\GitHub\Softellect\Vpn\`
- **No duplicate code**: reuse and generalize existing `netsh` runner logic (currently in `WinTunAdapter.SetIpAddress`).
- F# identifiers use **camelCase**.
- C# continues to use `IPAddress` only where it already does; F# continues to use `IpAddress` wrappers.
- If you see an inconsistency or ambiguity in this spec vs. the repo code, **ask for confirmation before implementing**.

## What to change

### 1) Generalize netsh runner in WinTunAdapter.cs

File: `C:\GitHub\Softellect\Vpn\Interop\WinTunAdapter.cs`

Currently `SetIpAddress(...)` shells out to `netsh` directly. Refactor so there is **one** shared helper used by all netsh calls, e.g. a private method that executes:

- `FileName = "netsh"`
- `Arguments = <passed arguments>`
- `UseShellExecute = false`
- `RedirectStandardOutput = true`
- `RedirectStandardError = true`
- `CreateNoWindow = true`
- Wait up to 5 seconds, treat non-zero exit code as failure and return stderr text

Then:

- Keep `SetIpAddress(...)`, but implement it using the shared helper.
- Add a new method to set DNS on this adapter (also using the shared helper):
  - `SetDnsServer(Primitives.IpAddress dnsServerIp) : Result<Unit>`
  - netsh command:
    - `interface ip set dns name="<adapterName>" static <dnsServerIp.value>`
- Add new methods to add routes via this adapter (also using the shared helper):
  - `AddRoute(Primitives.IpAddress destination, Primitives.IpAddress mask, Primitives.IpAddress gateway, int metric) : Result<Unit>`
  - Use `netsh interface ipv4 add route` and explicitly target the adapter by name.
  - Ensure idempotency: if the route already exists, treat it as success (do not fail the whole startup). Implement this by detecting the “already exists” style failure from stderr and returning success.

Also add these “optional but helpful” methods (implement if trivial using the same helper; do not add new helpers):
- `FlushDns() : Result<Unit>` using `ipconfig /flushdns` (via `ProcessStartInfo`, same style).
- `SetInterfaceMetric(int metric) : Result<Unit>` using a netsh command if you already have a reliable one for interface metric; if unsure, **ask for confirmation** instead of guessing.

### 2) Configure DNS + default routes after tunnel IP is set

File: `C:\GitHub\Softellect\Vpn\Client\Tunnel.fs`

Update `TunnelConfig` to include:
- `gatewayIp : IpAddress` (VPN gateway inside the VPN subnet; for current MVP it is the server VPN IP)
- `dnsServerIp : IpAddress` (for current MVP use the same as gateway)

In `Tunnel.start()` after `SetIpAddress(config.assignedIp.value, config.subnetMask)` succeeds:

1) Call `SetDnsServer(config.dnsServerIp)`
2) Add split default routes through the gateway (this is required):
   - `0.0.0.0/1` via `gatewayIp`
   - `128.0.0.0/1` via `gatewayIp`
   Use masks `128.0.0.0` for both.
3) If any step fails (DNS or routes), cleanly dispose the adapter/session and return `Error`.

Logging:
- Add `logInfo` lines for:
  - setting DNS
  - adding each route
- Add `logError` on failures including stderr message returned by the helper.

### 3) Wire config values from the client service

File: `C:\GitHub\Softellect\Vpn\Client\Service.fs`

In `startTunnel assignedIp`, extend the `TunnelConfig` you pass to include:
- `gatewayIp = Ip4 "<serverVpnIp>"` (use the existing constant/value in the repo that represents the server VPN IP; do not introduce a new hard-coded literal if that constant already exists)
- `dnsServerIp = gatewayIp`

Do not change the kill-switch logic in this step.

## Expected outcome

After this change:
- With kill-switch enabled, the machine routes internet traffic through the WinTun adapter.
- DNS resolution uses the VPN DNS server (the VPN gateway IP for MVP).
- The client can browse / resolve domains while the kill-switch still blocks traffic outside the VPN.

## Files to modify (expected)

- `C:\GitHub\Softellect\Vpn\Interop\WinTunAdapter.cs`
- `C:\GitHub\Softellect\Vpn\Client\Tunnel.fs`
- `C:\GitHub\Softellect\Vpn\Client\Service.fs`

No other files unless required by compilation.
