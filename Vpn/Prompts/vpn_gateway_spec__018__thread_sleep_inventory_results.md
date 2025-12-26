# vpn_gateway_spec__018__thread_sleep_inventory_results.md

## Thread.Sleep Inventory

Total call sites found: **12**

---

### Call Sites (sorted by file path, then line number)

- `Softellect.Vpn.Client.Service.VpnClientService` :: sendLoop  |  poll backoff when no packets  |  Client\Service.fs:149
- `Softellect.Vpn.Client.Service.VpnClientService` :: sendLoop  |  poll backoff when tunnel not ready  |  Client\Service.fs:150
- `Softellect.Vpn.Client.Service.VpnClientService` :: sendLoop  |  retry delay after send error  |  Client\Service.fs:154
- `Softellect.Vpn.Client.Service.VpnClientService` :: receiveLoop  |  poll backoff when no packets received  |  Client\Service.fs:173
- `Softellect.Vpn.Client.Service.VpnClientService` :: receiveLoop  |  retry delay after receive error  |  Client\Service.fs:176
- `Softellect.Vpn.Client.Service.VpnClientService` :: receiveLoop  |  poll backoff when tunnel not ready  |  Client\Service.fs:178
- `Softellect.Vpn.Client.Service.VpnClientService` :: receiveLoop  |  retry delay after exception  |  Client\Service.fs:182
- `Softellect.Vpn.Client.Tunnel.Tunnel` :: receiveLoop  |  poll backoff when no packet  |  Client\Tunnel.fs:65
- `Softellect.Vpn.Client.Tunnel.Tunnel` :: receiveLoop  |  retry delay after exception  |  Client\Tunnel.fs:69
- `Softellect.Vpn.Server.ExternalInterface.ExternalGateway` :: receiveLoop  |  retry delay after socket error  |  Server\ExternalInterface.fs:122
- `Softellect.Vpn.Server.PacketRouter.PacketRouter` :: receiveLoop  |  poll backoff when no packet  |  Server\PacketRouter.fs:212
- `Softellect.Vpn.Server.PacketRouter.PacketRouter` :: receiveLoop  |  retry delay after exception  |  Server\PacketRouter.fs:216
