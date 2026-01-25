namespace Softellect.Vpn.Client

open System
open Softellect.Sys.Core
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open Softellect.Vpn.Interop
open Softellect.Vpn.Core.Primitives

module NetworkDetector =
    let private tryParseDefaultRoute (routeOutput: string) =
        // Parse output of: netsh interface ipv4 show route
        // Looking for a line with destination 0.0.0.0/0
        // Format: Publish  Type      Met  Prefix                    Idx  Gateway/Interface Name
        //         No       Manual    1    0.0.0.0/0                   6  192.168.2.1
        let lines = routeOutput.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)

        lines
        |> Array.tryPick (fun line ->
            let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
            // Look for 0.0.0.0/0 in the line
            let hasDefaultRoute = parts |> Array.exists (fun p -> p = "0.0.0.0/0")
            if hasDefaultRoute && parts.Length >= 6 then
                // The last part is gateway IP, second to last is interface index
                let gateway = parts[parts.Length - 1]
                let interfaceIdx = parts[parts.Length - 2]
                match Int32.TryParse(interfaceIdx) with
                | true, idx -> Some (idx, gateway)
                | false, _ ->
                    // Maybe gateway is at a different position, try to find IP-like strings
                    let ipParts = parts |> Array.filter (fun p ->
                        match IpAddress.tryCreate p with
                        | Some _ -> true
                        | None -> false)
                    if ipParts.Length > 0 then
                        // Try to get interface index from parts before gateway
                        let idxCandidates = parts |> Array.choose (fun p ->
                            match Int32.TryParse(p) with
                            | true, v when v > 0 && v < 1000 -> Some v
                            | _ -> None)
                        if idxCandidates.Length > 0 then
                            Some (idxCandidates[0], ipParts[ipParts.Length - 1])
                        else None
                    else None
            else None)


    let private tryGetInterfaceName (interfaceIdx: int) =
        // Parse output of: netsh interface ipv4 show interfaces
        // Format: Idx     Met         MTU          State                Name
        //           6      25        1500  connected     Wi-Fi
        match tryExecuteFile (FileName "netsh") "interface ipv4 show interfaces" with
        | Ok (_, output) ->
            let lines = output.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            lines
            |> Array.tryPick (fun line ->
                let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length >= 5 then
                    match Int32.TryParse(parts[0]) with
                    | true, idx when idx = interfaceIdx ->
                        // Interface name is the last part (may contain spaces, so take everything after State)
                        // Find "connected" or similar state words, name is after
                        let stateIdx = parts |> Array.tryFindIndex (fun p ->
                            p.ToLower() = "connected" || p.ToLower() = "disconnected")
                        match stateIdx with
                        | Some si when si < parts.Length - 1 ->
                            let nameParts = parts |> Array.skip (si + 1)
                            Some (String.Join(" ", nameParts))
                        | _ -> Some parts[parts.Length - 1]
                    | _ -> None
                else None)
            |> Option.map Ok
            |> Option.defaultValue (Error $"Interface with index {interfaceIdx} not found")
        | Error e -> Error $"%A{e}"


    let tryDetectPhysicalNetwork1 () =
        Logger.logInfo "Detecting physical network configuration using netsh..."

        match tryExecuteFile (FileName "netsh") "interface ipv4 show route" with
        | Ok (_, routeOutput) ->
            match tryParseDefaultRoute routeOutput with
            | Some (interfaceIdx, gatewayIp) ->
                Logger.logInfo $"Detected default gateway: '{gatewayIp}' (interface index: {interfaceIdx}) using netsh."

                match tryGetInterfaceName interfaceIdx with
                | Ok interfaceName ->
                    Logger.logInfo $"Detected interface name: '{interfaceName}'."

                    match IpAddress.tryCreate gatewayIp with
                    | Some ip -> Ok (ip, interfaceName)
                    | None ->
                        let errMsg = $"Invalid gateway IP format: '{gatewayIp}'."
                        Logger.logError errMsg
                        Error errMsg
                | Error e ->
                    let errMsg = $"Failed to get interface name: '{e}'."
                    Logger.logError errMsg
                    Error errMsg
            | None ->
                let errMsg = "Could not find default route (0.0.0.0/0) in routing table"
                Logger.logError errMsg
                Error errMsg
        | Error e ->
            let errMsg = $"Failed to get routing table: '%A{e}'."
            Logger.logError errMsg
            Error errMsg


    let tryDetectPhysicalNetwork2 () =
        Logger.logInfo "Detecting physical network configuration using Windows API..."
        try
            let struct (ip, name) = PhysicalNetworkDetector.GetPhysicalGatewayAndInterface()
            Logger.logInfo $"Detected default gateway: '{ip}', interface name: '{name}' using Windows API."
            Ok (Ip4 ip, name)
        with
        | e ->
            Logger.logError $"Exception: '{e}'."
            Error $"{e}"


    let tryDetectPhysicalNetwork () =
        let v1 = tryDetectPhysicalNetwork1()
        let v2 = tryDetectPhysicalNetwork2()

        match v1, v2 with
        | Ok d1, Ok d2 ->
            match d1 = d2 with
            | true -> Ok { gatewayIp = fst d1; interfaceName = snd d1 }
            | false ->
                Logger.logError $"d1: '%A{d1}' does not match d2: '%A{d2}'."
                Ok { gatewayIp = fst d1; interfaceName = snd d1 }
        | Error e1, Ok d2 ->
            Logger.logError $"V1 error: '{e1}'."
            Ok { gatewayIp = fst d2; interfaceName = snd d2 }
        | Ok d1, Error e2 ->
            Logger.logError $"V2 error: '{e2}'."
            Ok { gatewayIp = fst d1; interfaceName = snd d1 }
        | Error e1, Error e2 ->
            let errMsg = $"V1 error: '{e1}', V2 error: '{e2}'."
            Logger.logError errMsg
            Error errMsg
