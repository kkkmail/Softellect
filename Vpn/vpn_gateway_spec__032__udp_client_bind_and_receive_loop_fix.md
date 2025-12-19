# vpn_gateway_spec__032__udp_client_bind_and_receive_loop_fix.md

## Goal

Fix the UDP client so it can **receive** datagrams reliably. The current client crashes immediately with:

> `You must call the Bind method before performing this operation.`

This happens because the client starts a background `Receive()` loop on a UDP socket that was never bound (and not connected).

**Do not change WCF code. Do not touch server logic except for logging if absolutely necessary.**

Work only in:

- `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`

## Required changes

### 1) Bind + connect the UDP socket before starting background loops

In `VpnUdpClient` constructor, replace:

```fs
let udpClient = new System.Net.Sockets.UdpClient()
```

with the following exact approach:

- Bind to an ephemeral local port (port 0).
- Connect to the server endpoint (so `Receive()` is valid and the socket is associated with a remote endpoint).
- After this, you may use `udpClient.Send(...)` without passing endpoint.

Use this exact code:

```fs
let udpClient = new System.Net.Sockets.UdpClient()

do
    // Bind to an ephemeral local port so Receive() works.
    udpClient.Client.Bind(IPEndPoint(IPAddress.Any, 0))

    // Connect fixes the remote endpoint and enables Receive() on this socket.
    udpClient.Connect(serverEndpoint)

    // Keep timeout so the loop can check cancellation periodically.
    udpClient.Client.ReceiveTimeout <- CleanupIntervalMs

    Logger.logInfo $"VpnUdpClient created - Server: {serverIp}:{serverPort}, ClientId: {clientId.value}, Local={udpClient.Client.LocalEndPoint}"
```

Notes:
- Keep `CleanupIntervalMs` as the receive timeout.
- Leave `serverEndpoint` as-is.
- Do NOT start `Task.Run(receiveLoop)` before this bind/connect block.

### 2) Fix receiveLoop remote endpoint handling

In `receiveLoop`, replace:

```fs
let mutable remoteEp = serverEndpoint
let data = udpClient.Receive(&remoteEp)
```

with:

```fs
let mutable remoteEp = IPEndPoint(IPAddress.Any, 0)
let data = udpClient.Receive(&remoteEp)
```

Reason: `Receive(&remoteEp)` expects the variable to be an arbitrary endpoint holder. Initializing it to `serverEndpoint` is unnecessary and confusing.

### 3) After Connect(), use Send() not Send(datagram,...,endpoint)

In `sendRequest`, replace:

```fs
udpClient.Send(datagram, datagram.Length, serverEndpoint) |> ignore
```

with:

```fs
udpClient.Send(datagram, datagram.Length) |> ignore
```

Because we now call `udpClient.Connect(serverEndpoint)` once at startup.

### 4) Keep everything else unchanged

Do NOT refactor requestId demux logic in this change.
Do NOT modify `Softellect.Vpn.Core.UdpProtocol`.
Do NOT modify `Softellect.Vpn.Server.UdpServer`.

## Acceptance criteria

After this change:

1. Client no longer logs: `You must call the Bind method before performing this operation.`
2. Client authenticate succeeds (as it did previously when firewall was fixed).
3. Ping continues to work.
4. If any further errors occur, add **one** trace log in the client receive loop showing remoteEp and the parsed header fields:
   - msgType
   - clientId
   - requestId
   - payloadLen
   (But do not change protocol logic.)

## Explicit constraints for CC

- Only edit `C:\GitHub\Softellect\Vpn\Client\UdpClient.fs`.
- Do not change public signatures.
- Do not introduce new modules.
- Do not add “optional” alternatives.
