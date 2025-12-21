namespace Softellect.Vpn.Server

open System
open System.Diagnostics
open System.Net
open System.Threading
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.PacketDebug
open Softellect.Vpn.Core.Primitives
open Softellect.Vpn.Core.UdpProtocol
open Softellect.Vpn.Interop
open Softellect.Vpn.Server.DnsProxy
open Softellect.Vpn.Server.IcmpProxy
open Softellect.Vpn.Server.ExternalInterface
open Softellect.Vpn.Server.Nat

module PacketRouter =

    /// Measure elapsed time for an action, updating tick and count accumulators.
    let inline timed (sw: Stopwatch) (timeAcc: int64 byref) (countAcc: int64 byref) (action: unit -> 'T) : 'T =
        sw.Restart()
        let r = action()
        sw.Stop()
        timeAcc <- timeAcc + sw.ElapsedTicks
        countAcc <- countAcc + 1L
        r

    /// Format percentage as "00.0000" from bucket ticks and calendar ticks.
    let formatPct (bucketTicks: int64) (calTicks: int64) =
        if calTicks = 0L then "00.0000"
        else
            let pct1e6 = bucketTicks * 1_000_000L / calTicks
            let whole = pct1e6 / 10_000L
            let frac = pct1e6 % 10_000L
            $"%02d{whole}.%04d{frac}"

    /// State for the receive loop: stopwatches, tick/count accumulators, and counters.
    [<NoEquality; NoComparison>]
    type ReceiveLoopState =
        {
            // Stopwatches (created once, reused)
            swCalendar : Stopwatch
            swWait : Stopwatch
            swDrain : Stopwatch
            swProcess : Stopwatch

            // Per-interval tick accumulators
            mutable lastStatsTicks : int64
            mutable waitTicks : int64
            mutable drainTicks : int64
            mutable processTicks : int64

            // Per-interval count accumulators
            mutable waitCount : int64
            mutable drainCount : int64
            mutable processCount : int64

            // Per-interval counters
            mutable waitWakeups : int64
            mutable waitTimeouts : int64
            mutable waitCancels : int64
            mutable emptyWakeups : int64
            mutable packetsRx : int64
        }

    /// Constants for receive loop timing.
    let [<Literal>] statsIntervalMs = PushStatsIntervalMs
    let [<Literal>] waitTimeoutMs = 250
    let [<Literal>] maxPacketsPerWakeup = 4096
    let [<Literal>] cancelCheckEveryPackets = 256

    /// Create initial receive loop state with all stopwatches and accumulators.
    let mkReceiveLoopState () =
        {
            swCalendar = Stopwatch.StartNew()
            swWait = Stopwatch()
            swDrain = Stopwatch()
            swProcess = Stopwatch()
            lastStatsTicks = 0L
            waitTicks = 0L
            drainTicks = 0L
            processTicks = 0L
            waitCount = 0L
            drainCount = 0L
            processCount = 0L
            waitWakeups = 0L
            waitTimeouts = 0L
            waitCancels = 0L
            emptyWakeups = 0L
            packetsRx = 0L
        }

    /// Reset all per-interval accumulators after logging.
    let resetInterval (st: ReceiveLoopState) (curCalTicks: int64) =
        st.lastStatsTicks <- curCalTicks
        st.waitTicks <- 0L
        st.drainTicks <- 0L
        st.processTicks <- 0L
        st.waitCount <- 0L
        st.drainCount <- 0L
        st.processCount <- 0L
        st.waitWakeups <- 0L
        st.waitTimeouts <- 0L
        st.waitCancels <- 0L
        st.emptyWakeups <- 0L
        st.packetsRx <- 0L

    /// Log stats if interval elapsed, then reset per-interval accumulators.
    let logStatsIfNeeded (st: ReceiveLoopState) =
        let curCalTicks = st.swCalendar.ElapsedTicks
        let intervalCalTicks = curCalTicks - st.lastStatsTicks
        let intervalMs = intervalCalTicks * 1000L / Stopwatch.Frequency
        if intervalMs >= statsIntervalMs then
            let elapsed = st.swCalendar.Elapsed
            let calStr = $"%02d{int elapsed.TotalMinutes}:%02d{elapsed.Seconds}.%03d{elapsed.Milliseconds}"
            let waitPct = formatPct st.waitTicks intervalCalTicks
            let drainPct = formatPct st.drainTicks intervalCalTicks
            let procPct = formatPct st.processTicks intervalCalTicks
            Logger.logInfo $"PacketRouter recv: t={calStr} wait={waitPct} drain={drainPct} proc={procPct} | wk={st.waitWakeups} to={st.waitTimeouts} cancel={st.waitCancels} empty={st.emptyWakeups} rx={st.packetsRx}"
            resetInterval st curCalTicks

    /// Convert IP address string to uint32 (network byte order)
    let ipToUInt32 (ip: IpAddress) =
        let parts = ip.value.Split('.')
        if parts.Length = 4 then
            let b0 = byte parts[0]
            let b1 = byte parts[1]
            let b2 = byte parts[2]
            let b3 = byte parts[3]
            (uint32 b0 <<< 24) ||| (uint32 b1 <<< 16) ||| (uint32 b2 <<< 8) ||| uint32 b3
        else
            0u

    type PacketRouterConfig =
        {
            vpnSubnet : VpnSubnet
            adapterName : string
            serverVpnIp : VpnIpAddress
            serverPublicIp : IpAddress
        }

        static member defaultValue =
            {
                vpnSubnet = VpnSubnet.defaultValue
                adapterName = AdapterName
                serverVpnIp = serverVpnIp
                serverPublicIp = Ip4 "0.0.0.0" // Must be configured with actual public IP
            }


    type PacketRouter(config: PacketRouterConfig, registry: ClientRegistry.ClientRegistry) =
        let mutable adapter : WinTunAdapter option = None
        let mutable running = false
        let mutable receiveThread : Thread option = None
        let cts = new CancellationTokenSource()
        let mutable readEventWarningLogged = false

        // ---- NAT / External interface setup ----

        let parseSubnetToUInt32 (subnet: string) =
            let parts = subnet.Split('/')
            if parts.Length = 2 then
                match IpAddress.tryCreate(parts[0]), Int32.TryParse(parts[1]) with
                | Some ip, (true, prefix) ->
                    let subnetUint = ipToUInt32 ip
                    let maskUint =
                        if prefix = 0 then 0u
                        else UInt32.MaxValue <<< (32 - prefix)
                    Some (subnetUint, maskUint)
                | _ -> None
            else
                None

        // Parse VPN subnet for NAT
        let vpnSubnetUint, vpnMaskUint =
            match parseSubnetToUInt32 config.vpnSubnet.value with
            | Some (s, m) -> s, m
            | None ->
                Logger.logWarn $"Failed to parse VPN subnet '{config.vpnSubnet.value}', using defaults"
                (0x0A424D00u, 0xFFFFFF00u) // 10.66.77.0/24

        // Parse external IP for NAT
        let externalIpUint = ipToUInt32 config.serverPublicIp

        // Parse server VPN IP for DNS proxy
        let serverVpnIpUint = ipToUInt32 config.serverVpnIp.value

        // Check if an IP (in network byte order uint32) is inside VPN subnet
        let isInsideVpnSubnet (ip: uint32) =
            (ip &&& vpnMaskUint) = (vpnSubnetUint &&& vpnMaskUint)

        // External gateway for internet traffic
        let externalGateway =
            let externalConfig : ExternalConfig =
                {
                    serverPublicIp = IPAddress.Parse(config.serverPublicIp.value)
                }
            new ExternalGateway(externalConfig)

        let parseSubnet (subnet: string) =
            let parts = subnet.Split('/')
            if parts.Length = 2 then
                match IpAddress.tryCreate(parts[0]), Int32.TryParse(parts[1]) with
                | Some ip, (true, prefix) ->
                    let maskBytes =
                        if prefix = 0 then 0u
                        else UInt32.MaxValue <<< (32 - prefix)
                    Some (ip, maskBytes)
                | _ -> None
            else
                None

        let getDestinationIp (packet: byte[]) =
            if packet.Length >= 20 then
                // IPv4: destination IP is at bytes 16-19
                IpAddress.tryCreate $"{packet[16]}.{packet[17]}.{packet[18]}.{packet[19]}"
            else
                None

        let getSourceIp (packet: byte[]) =
            if packet.Length >= 16 then
                // IPv4: source IP is at bytes 12-15
                IpAddress.tryCreate $"{packet[12]}.{packet[13]}.{packet[14]}.{packet[15]}"
            else
                None

        // Get destination IP as uint32 for NAT comparison
        let getDestinationIpUInt32 (packet: byte[]) =
            if packet.Length >= 20 then
                Some (
                    (uint32 packet[16] <<< 24) |||
                    (uint32 packet[17] <<< 16) |||
                    (uint32 packet[18] <<< 8) |||
                    uint32 packet[19]
                )
            else
                None

        let findClientByIp (ip: IpAddress) =
            registry.getAllPushSessions()
            |> List.tryFind (fun s -> s.assignedIp.value.Equals(ip))

        // Helper: reset the read event if possible (to avoid stuck signaled state)
        let resetReadEventIfPossible (readEvent: WaitHandle) =
            match readEvent with
            | :? EventWaitHandle as ewh ->
                try ewh.Reset() |> ignore with _ -> ()
            | _ -> ()

        // Helper: process a single IPv4 packet (DNS proxy, ICMP proxy, NAT, VPN routing)
        let processPacket (packet: byte[]) =
            let v = getIpVersion packet
            match v with
            | 4 ->
                // IPv4 packet - check for DNS proxy first, then route based on destination
                match tryParseDnsQuery serverVpnIpUint packet with
                | Some (clientIp, clientPort, dnsPayload) ->
                    // DNS query to VPN gateway - forward to upstream and enqueue reply to client
                    let clientIpStr = $"{byte (clientIp >>> 24)}.{byte (clientIp >>> 16)}.{byte (clientIp >>> 8)}.{byte clientIp}"
                    match IpAddress.tryCreate clientIpStr with
                    | Some clientIpAddr ->
                        match findClientByIp clientIpAddr with
                        | Some session ->
                            match forwardDnsQuery serverVpnIpUint clientIp clientPort dnsPayload with
                            | Some replyPacket ->
                                // Enqueue DNS reply directly to the client (not into TUN)
                                registry.enqueuePacketForClient(session.clientId, replyPacket) |> ignore
                                Logger.logTrace (fun () -> $"DNSPROXY: enqueued reply to client {session.clientId.value}, len={replyPacket.Length}")
                            | None ->
                                // Timeout or error - already logged by DnsProxy
                                ()
                        | None ->
                            Logger.logTrace (fun () -> $"DNSPROXY: no client found for source IP {clientIpStr}")
                    | None ->
                        Logger.logWarn $"DNSPROXY: failed to parse client IP {clientIpStr}"
                | None ->
                    // Not a DNS query - check for ICMP Echo Request to external address
                    match tryHandleOutbound vpnSubnetUint vpnMaskUint externalIpUint packet with
                    | Some icmpPacket ->
                        // ICMP Echo Request to external address - send via external gateway
                        externalGateway.sendOutbound(icmpPacket)
                    | None ->
                        // Not ICMP proxy - proceed with normal routing
                        match getDestinationIpUInt32 packet with
                        | Some destIpUint ->
                            if isInsideVpnSubnet destIpUint then
                                // Destination is inside VPN subnet - route to VPN client
                                match getDestinationIp packet with
                                | Some destIp ->
                                    match getSourceIp packet with
                                    | Some srcIp ->
                                        match findClientByIp destIp with
                                        | Some session ->
                                            registry.enqueuePacketForClient(session.clientId, packet) |> ignore
                                            Logger.logTrace (fun () -> $"Routing packet: src={srcIp}, dst={destIp}, size={packet.Length} bytes -> client {session.clientId.value}, packet=%A{(summarizePacket packet)}")
                                        | None ->
                                            Logger.logTrace (fun () -> $"No client found for destination IP: {destIp}")
                                    | None ->
                                        Logger.logTrace (fun () -> "Could not parse source IP from packet")
                                | None ->
                                    Logger.logTrace (fun () -> "Could not parse destination IP from packet")
                            else
                                // Destination is outside VPN subnet - NAT and forward to external network
                                match translateOutbound (vpnSubnetUint, vpnMaskUint) externalIpUint packet with
                                | Some natPacket ->
                                    externalGateway.sendOutbound(natPacket)
                                | None ->
                                    Logger.logTrace (fun () -> "NAT outbound: packet dropped (no translation)")
                        | None ->
                            Logger.logTrace (fun () -> "Could not parse destination IP from packet")
            | 6 ->
                // IPv6 packet - drop (VPN is IPv4-only)
                Logger.logTrace (fun () -> $"PacketRouter: dropping IPv6 packet from WinTun, len={packet.Length}, packet=%A{(summarizePacket packet)}")
            | _ ->
                // Unknown/malformed - drop silently
                ()

        // Helper: wait for readEvent or cancellation (timed).
        let waitForEvent (st: ReceiveLoopState) (waitHandles: WaitHandle[]) =
            timed st.swWait &st.waitTicks &st.waitCount (fun () ->
                WaitHandle.WaitAny(waitHandles, waitTimeoutMs))

        // Helper: drain all available packets from WinTun adapter.
        // Times ReceivePacket calls in drainTicks, processPacket calls in processTicks (non-overlapping).
        let drainPackets (st: ReceiveLoopState) (adp: WinTunAdapter) (readEvent: WaitHandle) =
            let mutable processedThisWakeup = 0

            // Time first ReceivePacket call
            let first = timed st.swDrain &st.drainTicks &st.drainCount (fun () -> adp.ReceivePacket())

            if isNull first || first.Length = 0 then
                // Spurious wakeup / stuck signaled
                st.emptyWakeups <- st.emptyWakeups + 1L
                resetReadEventIfPossible readEvent
            else
                // Process first packet
                timed st.swProcess &st.processTicks &st.processCount (fun () -> processPacket first)
                st.packetsRx <- st.packetsRx + 1L
                processedThisWakeup <- processedThisWakeup + 1

                // Drain remaining packets
                let mutable continueLoop = true
                while continueLoop do
                    // Check cap
                    if processedThisWakeup >= maxPacketsPerWakeup then
                        resetReadEventIfPossible readEvent
                        Thread.Yield() |> ignore
                        continueLoop <- false
                    // Check cancellation every cancelCheckEveryPackets packets
                    elif processedThisWakeup % cancelCheckEveryPackets = 0 && cts.Token.IsCancellationRequested then
                        resetReadEventIfPossible readEvent
                        continueLoop <- false
                    else
                        let packet = timed st.swDrain &st.drainTicks &st.drainCount (fun () -> adp.ReceivePacket())

                        if isNull packet || packet.Length = 0 then
                            resetReadEventIfPossible readEvent
                            continueLoop <- false
                        else
                            timed st.swProcess &st.processTicks &st.processCount (fun () -> processPacket packet)
                            st.packetsRx <- st.packetsRx + 1L
                            processedThisWakeup <- processedThisWakeup + 1

        // Helper: handle the result of WaitAny.
        let handleWaitResult (st: ReceiveLoopState) (adp: WinTunAdapter) (readEvent: WaitHandle) (waitResult: int) =
            if waitResult = WaitHandle.WaitTimeout then
                // Timeout - no event fired
                st.waitTimeouts <- st.waitTimeouts + 1L
                logStatsIfNeeded st
            elif waitResult = 1 || cts.Token.IsCancellationRequested then
                // Cancellation signaled
                st.waitCancels <- st.waitCancels + 1L
            else
                // readEvent signaled (index 0)
                st.waitWakeups <- st.waitWakeups + 1L
                drainPackets st adp readEvent
                logStatsIfNeeded st

        let receiveLoop () =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                let readEvent = adp.GetReadWaitHandle()
                match readEvent with
                | null ->
                    if not readEventWarningLogged then
                        Logger.logWarn "PacketRouter: GetReadWaitHandle returned null, exiting receive loop"
                        readEventWarningLogged <- true
                | _ ->
                    let waitHandles = [| readEvent; cts.Token.WaitHandle |]
                    let st = mkReceiveLoopState()

                    while running && not cts.Token.IsCancellationRequested do
                        try
                            let waitResult = waitForEvent st waitHandles
                            handleWaitResult st adp readEvent waitResult
                        with
                        | ex ->
                            Logger.logError $"Error in receive loop: {ex.Message}"
            | _ -> Logger.logWarn "Adapter not ready for receive loop"

        let getErrorMessage (result: Softellect.Vpn.Interop.Result<Unit>) =
            match result.Error with
            | null -> "Unknown error"
            | err -> err

        let getErrorMessageT (result: Softellect.Vpn.Interop.Result<WinTunAdapter>) =
            match result.Error with
            | null -> "Unknown error"
            | err -> err

        member _.start() =
            Logger.logInfo $"Starting packet router with adapter: {config.adapterName}"

            let createResult = WinTunAdapter.Create(config.adapterName, AdapterName, System.Nullable<Guid>())
            if createResult.IsSuccess then
                adapter <- Some createResult.Value

                let sessionResult = createResult.Value.StartSession()
                if sessionResult.IsSuccess then
                    // Set the IP address on the adapter
                    let serverIp = config.serverVpnIp.value
                    let subnetMask = Ip4 "255.255.255.0"

                    let ipResult = createResult.Value.SetIpAddress(serverIp, subnetMask)
                    let mtuResult = createResult.Value.SetMtu(MtuSize)
                    Logger.logInfo $"Server - ipResult: {ipResult.IsSuccess}, mtuResult: {mtuResult.IsSuccess}."

                    if ipResult.IsSuccess then
                        running <- true

                        // Start external gateway with inbound callback (ICMP proxy first, then NAT)
                        externalGateway.start(fun rawPacket ->
                            // Called when external gateway receives a packet from internet
                            // Check for ICMP Echo Reply first
                            match tryHandleInbound rawPacket with
                            | Some (clientIpUint, icmpPacket) ->
                                // ICMP Echo Reply - enqueue to the correct client
                                let clientIpStr = $"{byte (clientIpUint >>> 24)}.{byte (clientIpUint >>> 16)}.{byte (clientIpUint >>> 8)}.{byte clientIpUint}"
                                match IpAddress.tryCreate clientIpStr with
                                | Some clientIpAddr ->
                                    match findClientByIp clientIpAddr with
                                    | Some session ->
                                        registry.enqueuePacketForClient(session.clientId, icmpPacket) |> ignore
                                        Logger.logTrace (fun () -> $"ICMPPROXY: enqueued reply to client {session.clientId.value}, size={icmpPacket.Length} bytes")
                                    | None ->
                                        Logger.logTrace (fun () -> $"ICMPPROXY: no client session for IP {clientIpStr}")
                                | None ->
                                    Logger.logTrace (fun () -> $"ICMPPROXY: failed to parse client IP {clientIpStr}")
                            | None ->
                                // Not ICMP proxy - fall through to NAT
                                match translateInbound externalIpUint rawPacket with
                                | Some translated ->
                                    // Enqueue translated packet directly to the VPN client
                                    match getDestinationIpUInt32 translated with
                                    | Some destIpUint ->
                                        let destIpStr = $"{byte (destIpUint >>> 24)}.{byte (destIpUint >>> 16)}.{byte (destIpUint >>> 8)}.{byte destIpUint}"
                                        match IpAddress.tryCreate destIpStr with
                                        | Some destIpAddr ->
                                            match findClientByIp destIpAddr with
                                            | Some session ->
                                                registry.enqueuePacketForClient(session.clientId, translated) |> ignore
                                                // Logger.logTrace (fun () -> $"HEAVY LOG - NAT inbound: enqueued packet to client {session.clientId.value}, size={translated.Length} bytes")
                                            | None ->
                                                Logger.logTrace (fun () -> $"NAT inbound: no client session for destination IP {destIpStr}")
                                        | None ->
                                            Logger.logTrace (fun () -> $"NAT inbound: failed to parse destination IP {destIpStr}")
                                    | None ->
                                        Logger.logTrace (fun () -> "NAT inbound: could not extract destination IP from translated packet")
                                | None ->
                                    // Logger.logTrace (fun () -> "HEAVY LOG - NAT inbound: packet dropped (no mapping)")
                                    ()
                        )

                        let thread = Thread(ThreadStart(receiveLoop))
                        thread.IsBackground <- true
                        thread.Start()
                        receiveThread <- Some thread
                        Logger.logInfo $"Packet router started with IP: {serverIp}"
                        Ok ()
                    else
                        let errMsg = getErrorMessage ipResult
                        Logger.logError $"Failed to set IP address: {errMsg}"
                        createResult.Value.Dispose()
                        adapter <- None
                        Error $"Failed to set IP address: {errMsg}"
                else
                    let errMsg = getErrorMessage sessionResult
                    Logger.logError $"Failed to start session: {errMsg}"
                    createResult.Value.Dispose()
                    adapter <- None
                    Error $"Failed to start session: {errMsg}"
            else
                let errMsg = getErrorMessageT createResult
                Logger.logError $"Failed to create adapter: {errMsg}"
                Error $"Failed to create adapter: {errMsg}"

        member _.stop() =
            Logger.logInfo "Stopping packet router"
            running <- false
            cts.Cancel()

            // Stop external gateway first
            externalGateway.stop()

            match receiveThread with
            | Some thread ->
                if thread.IsAlive then
                    thread.Join(TimeSpan.FromSeconds(5.0)) |> ignore
                receiveThread <- None
            | None -> ()

            match adapter with
            | Some adp ->
                adp.Dispose()
                adapter <- None
            | None -> ()

            Logger.logInfo "Packet router stopped"

        member _.injectPacket(packet: byte[]) =
            match adapter with
            | Some adp when adp.IsSessionActive ->
                let result = adp.SendPacket(packet)
                if result.IsSuccess then
                    Logger.logTrace (fun () -> $"Injected packet to TUN adapter, size={packet.Length} bytes, packet=%A{(summarizePacket packet)}")
                    Ok ()
                else Error (getErrorMessage result)
            | _ ->
                Error "Adapter not ready"

        member r.routeFromClient(packet: byte[]) =
            let v = getIpVersion packet

            match v with
            | 4 ->
                match getDestinationIpUInt32 packet with
                | None ->
                    Ok () // malformed/too short; ignore
                | Some destIpUint ->
                    if isInsideVpnSubnet destIpUint then
                        // Inside VPN subnet: inject into TUN so the existing router loop can deliver to clients.
                        match r.injectPacket(packet) with
                        | Ok () -> Ok ()
                        | Error msg -> Error msg
                    else
                        // Outside VPN subnet: check ICMP proxy first, then NAT + send out via external interface.
                        if externalIpUint = 0u then
                            Error "PacketRouter.routeFromClient: serverPublicIp is 0.0.0.0 (externalIpUint=0) - cannot NAT outbound."
                        else
                            // Check for ICMP Echo Request to external address
                            match tryHandleOutbound vpnSubnetUint vpnMaskUint externalIpUint packet with
                            | Some icmpPacket ->
                                // ICMP Echo Request to external address - send via external gateway
                                externalGateway.sendOutbound(icmpPacket)
                                Ok ()
                            | None ->
                                // Not ICMP proxy - use NAT
                                match translateOutbound (vpnSubnetUint, vpnMaskUint) externalIpUint packet with
                                | Some natPacket ->
                                    Logger.logTrace (fun () -> $"NAT outbound (2): forwarding packet to external network, size={natPacket.Length} bytes, natPacket: {(summarizePacket natPacket)}")
                                    externalGateway.sendOutbound(natPacket)
                                    Ok ()
                                | None ->
                                    Ok () // dropped by NAT (e.g. too short); ignore
            | 6 -> Ok () // IPv6 not supported
            | _ -> Ok () // unknown/empty


        member _.isRunning = running
