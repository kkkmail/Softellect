namespace Softellect.Vpn.Core

open System
open System.Net
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Softellect.Sys.Logging

module PacketDebug =

    /// Get IP version from packet (4 = IPv4, 6 = IPv6, 0 = empty/invalid)
    let getIpVersion (packet: byte[]) =
        if packet.Length = 0 then 0
        else int packet[0] >>> 4

    
    let inline b2u16 (a: byte) (b: byte) = (uint16 a <<< 8) ||| uint16 b

    
    let inline readUInt16BE (buf: byte[]) (offset: int) =
        b2u16 buf[offset] buf[offset + 1]

    
    let inline readUInt32BE (buf: byte[]) (offset: int) =
        (uint32 buf[offset] <<< 24) ||| (uint32 buf[offset + 1] <<< 16) ||| (uint32 buf[offset + 2] <<< 8) ||| uint32 buf[offset + 3]

    
    let inline ipv4ToString (a: byte) (b: byte) (c: byte) (d: byte) =
        $"{a}.{b}.{c}.{d}"

    
    let inline isAsciiPrintable (b: byte) =
        b >= 32uy && b <= 126uy

    
    let private tryParseDnsName (payload: byte[]) (startOffset: int) =
        // Parse QNAME (labels) from DNS question section.
        // Returns (name, nextOffset) or ("<err>", startOffset) on failure.
        try
            let mutable off = startOffset
            let sb = System.Text.StringBuilder()
            let mutable first = true
            let mutable done' = false

            while not done' do
                if off >= payload.Length then
                    done' <- true
                    sb.Clear() |> ignore
                    sb.Append("<dns-qname-oob>") |> ignore
                else
                    let len = int payload[off]
                    off <- off + 1
                    if len = 0 then
                        done' <- true
                    elif (len &&& 0xC0) = 0xC0 then
                        // compression pointer in QNAME (unexpected in question but can appear)
                        if off >= payload.Length then
                            done' <- true
                            sb.Clear() |> ignore
                            sb.Append("<dns-qname-badptr>") |> ignore
                        else
                            // pointer consumes one more byte
                            off <- off + 1
                            done' <- true
                            if sb.Length = 0 then
                                sb.Append("<dns-qname-ptr>") |> ignore
                    else
                        if off + len > payload.Length then
                            done' <- true
                            sb.Clear() |> ignore
                            sb.Append("<dns-qname-trunc>") |> ignore
                        else
                            if not first then sb.Append('.') |> ignore
                            first <- false
                            // label bytes
                            for i in 0 .. (len - 1) do
                                let ch = payload[off + i]
                                if isAsciiPrintable ch then sb.Append(char ch) |> ignore
                                else sb.Append('?') |> ignore
                            off <- off + len

            let name =
                if sb.Length = 0 then "<root>"
                else sb.ToString()

            name, off
        with _ ->
            "<dns-qname-ex>", startOffset

    
    let private trySummarizeDns (udpPayload: byte[]) =
        // DNS header is 12 bytes
        if udpPayload.Length < 12 then
            "DNS <payload-too-short>"
        else
            let txid = readUInt16BE udpPayload 0
            let flags = readUInt16BE udpPayload 2
            let qd = readUInt16BE udpPayload 4
            let an = readUInt16BE udpPayload 6
            let ns = readUInt16BE udpPayload 8
            let ar = readUInt16BE udpPayload 10

            let qr = (flags &&& 0x8000us) <> 0us
            let rcode = int (flags &&& 0x000Fus)

            // Best-effort parse first question name
            let namePart =
                if qd = 0us then
                    ""
                else
                    let name, offAfterName = tryParseDnsName udpPayload 12
                    // question has QTYPE/QCLASS (4 bytes) after name
                    let qtqcOk = offAfterName + 4 <= udpPayload.Length
                    let qtype =
                        if qtqcOk then int (readUInt16BE udpPayload offAfterName) else 0
                    let qclass =
                        if qtqcOk then int (readUInt16BE udpPayload (offAfterName + 2)) else 0

                    // common qtype names (just a few)
                    let qtypeName =
                        match qtype with
                        | 1 -> "A"
                        | 28 -> "AAAA"
                        | 15 -> "MX"
                        | 16 -> "TXT"
                        | 5 -> "CNAME"
                        | 2 -> "NS"
                        | 12 -> "PTR"
                        | 33 -> "SRV"
                        | _ -> string qtype

                    $" qd=1 name={name} qtype={qtypeName} qclass={qclass}"

            if qr then
                $"DNS r txid=0x{txid:X4} qd={qd} an={an} ns={ns} ar={ar} rcode={rcode}{namePart}"
            else
                $"DNS q txid=0x{txid:X4} qd={qd} an={an} ns={ns} ar={ar}{namePart}"

    
    let private tcpFlagsToString (flags: byte) =
        let addIf (cond: bool) (s: string) (acc: ResizeArray<string>) =
            if cond then acc.Add(s)

        let a = ResizeArray<string>()
        addIf ((flags &&& 0x01uy) <> 0uy) "fin" a
        addIf ((flags &&& 0x02uy) <> 0uy) "syn" a
        addIf ((flags &&& 0x04uy) <> 0uy) "rst" a
        addIf ((flags &&& 0x08uy) <> 0uy) "psh" a
        addIf ((flags &&& 0x10uy) <> 0uy) "ack" a
        addIf ((flags &&& 0x20uy) <> 0uy) "urg" a
        if a.Count = 0 then "none" else String.Join("|", a)

    
    /// ICMP
    let private summarizeIPv4ICMP srcIp srcPort dstIp dstPort ihl ipPayloadLen (bytes: byte[]) =
        if bytes.Length < ihl + 4 then $"IPv4 ICMP: {srcIp} → {dstIp}, <icmp-too-short>, len={bytes.Length}"
        else
            let icmpType = bytes[ihl]
            let icmpCode = bytes[ihl + 1]

            // Echo req/reply has id/seq at +4/+6
            let echoExtra =
                if (icmpType = 8uy || icmpType = 0uy) && bytes.Length >= ihl + 8 then
                    let ident = readUInt16BE bytes (ihl + 4)
                    let seq = readUInt16BE bytes (ihl + 6)
                    $" id=0x{ident:X4} seq={seq}"
                else
                    ""

            let typeName =
                match icmpType with
                | 8uy -> "echo-req"
                | 0uy -> "echo-reply"
                | 3uy -> "dest-unreach"
                | 11uy -> "time-exceeded"
                | _ -> $"type={icmpType}"

            $"IPv4 ICMP: {srcIp} → {dstIp}, {typeName}, code={icmpCode},{echoExtra} ipPayload={ipPayloadLen}, len={bytes.Length}"
        
    
    // /// IPv4 - TCP
    // let private summarizeIPv4TCP srcIp srcPort dstIp dstPort ihl ipPayloadLen (bytes: byte[]) =
    //     if bytes.Length < ihl + 20 then $"IPv4 TCP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, <tcp-too-short>, len={bytes.Length}"
    //     else
    //         let tcpOff = ihl
    //         let seq = readUInt32BE bytes (tcpOff + 4)
    //         let ack = readUInt32BE bytes (tcpOff + 8)
    //         let dataOffsetWords = int (bytes[tcpOff + 12] >>> 4)
    //         let tcpHdrLen = dataOffsetWords * 4
    //         let flags = bytes[tcpOff + 13]
    //         let win = readUInt16BE bytes (tcpOff + 14)
    //         let appLen = max 0 (ipPayloadLen - tcpHdrLen)
    //
    //         $"IPv4 TCP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, flags={tcpFlagsToString flags}, seq={seq}, ack={ack}, win={win}, ipPayload={ipPayloadLen}, tcpHdr={tcpHdrLen}, app={appLen}, len={bytes.Length}"
        
    /// IPv4 - TCP
    let private summarizeIPv4TCP srcIp srcPort dstIp dstPort ihl ipPayloadLen (bytes: byte[]) =
        if bytes.Length < ihl + 20 then
            $"IPv4 TCP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, <tcp-too-short>, len={bytes.Length}"
        else
            let tcpOff = ihl
            let seq = readUInt32BE bytes (tcpOff + 4)
            let ack = readUInt32BE bytes (tcpOff + 8)
            let dataOffsetWords = int (bytes[tcpOff + 12] >>> 4)
            let tcpHdrLen = dataOffsetWords * 4
            let flags = bytes[tcpOff + 13]
            let win = readUInt16BE bytes (tcpOff + 14)
            let appLen = max 0 (ipPayloadLen - tcpHdrLen)
            let isPsh = (flags &&& 0x08uy) <> 0uy

            let hexdumpFirst64 (payloadOff: int) =
                if payloadOff >= bytes.Length then ""
                else
                    let n = min 64 (bytes.Length - payloadOff)
                    if n <= 0 then ""
                    else
                        let sbHex = System.Text.StringBuilder(n * 3)
                        let sbAsc = System.Text.StringBuilder(n)
                        
                        for i in 0 .. (n - 1) do
                            let b = bytes[payloadOff + i]
                            sbHex.AppendFormat("{0:X2}", b) |> ignore
                            if i <> n - 1 then sbHex.Append(' ') |> ignore
                            sbAsc.Append(if isAsciiPrintable b then char b else '.') |> ignore
                        $" pshDump[{n}]=<{sbHex}> ascii=<{sbAsc}>"

            let payloadOff = tcpOff + tcpHdrLen
            
            let pshDump =
                if isPsh && appLen > 0 && tcpHdrLen >= 20 then
                    hexdumpFirst64 payloadOff
                else ""

            $"IPv4 TCP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, flags={tcpFlagsToString flags}, seq={seq}, ack={ack}, win={win}, ipPayload={ipPayloadLen}, tcpHdr={tcpHdrLen}, app={appLen}, len={bytes.Length}{pshDump}"
        
    
    /// IPv4 - UDP
    let private summarizeIPv4UDP srcIp srcPort dstIp dstPort ihl ipPayloadLen (bytes: byte[]) =
        if bytes.Length < ihl + 8 then $"IPv4 UDP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, <udp-too-short>, len={bytes.Length}"
        else
            let udpLen = int (readUInt16BE bytes (ihl + 4))
            let payloadOffset = ihl + 8
            let payloadLenByIp = bytes.Length - payloadOffset
            let payloadLenByUdp = max 0 (udpLen - 8)
            let payloadLen = min payloadLenByIp payloadLenByUdp

            if payloadLen > 0 && (srcPort = 53 || dstPort = 53) then
                let udpPayload = Array.sub bytes payloadOffset payloadLen
                $"IPv4 UDP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, len={bytes.Length} {trySummarizeDns udpPayload}"
            else
                $"IPv4 UDP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, udpLen={udpLen}, ipPayload={ipPayloadLen}, len={bytes.Length}"

        
    /// IPv4
    let private summarizeIPv4Packet (bytes: byte[]) =
        let ihl = int (bytes[0] &&& 0x0Fuy) * 4
        if ihl < 20 || bytes.Length < ihl then $"<IPv4 header invalid: ihl={ihl}, len={bytes.Length}>"
        else
            let srcIp = ipv4ToString bytes[12] bytes[13] bytes[14] bytes[15]
            let dstIp = ipv4ToString bytes[16] bytes[17] bytes[18] bytes[19]
            let proto = bytes[9]

            // Total length from IPv4 header (may be smaller than buffer)
            let totalLen = int (readUInt16BE bytes 2)
            let effectiveTotalLen =
                if totalLen <= 0 then bytes.Length
                else min totalLen bytes.Length
            let ipPayloadLen = max 0 (effectiveTotalLen - ihl)

            // L4 ports (UDP/TCP) if present
            let srcPort, dstPort =
                if (proto = 6uy || proto = 17uy) && bytes.Length >= ihl + 4 then
                    let shp = readUInt16BE bytes ihl
                    let dhp = readUInt16BE bytes (ihl + 2)
                    int shp, int dhp
                else
                    0, 0

            match proto with
            | 1uy -> summarizeIPv4ICMP srcIp srcPort dstIp dstPort ihl ipPayloadLen bytes
            | 6uy -> summarizeIPv4TCP srcIp srcPort dstIp dstPort ihl ipPayloadLen bytes
            | 17uy -> summarizeIPv4UDP srcIp srcPort dstIp dstPort ihl ipPayloadLen bytes
            | _ ->
                // Other IPv4
                $"IPv4: {srcIp}:{srcPort} → {dstIp}:{dstPort}, proto={proto}, ipPayload={ipPayloadLen}, len={bytes.Length}"
    
    
    /// IPv6 (minimal)
    let private summarizeIPv6Packet (bytes: byte[]) =
        if bytes.Length < 40 then
            $"<IPv6 packet too short: {bytes.Length} bytes>"
        else
            let srcIp = IPAddress(bytes[8..23])
            let dstIp = IPAddress(bytes[24..39])
            let nextHeader = bytes[6]

            let srcPort, dstPort =
                if nextHeader = 6uy || nextHeader = 17uy then
                    let offs = 40
                    if bytes.Length >= offs + 4 then
                        let shp = b2u16 bytes[offs] bytes[offs + 1]
                        let dhp = b2u16 bytes[offs + 2] bytes[offs + 3]
                        int shp, int dhp
                    else 0, 0
                else 0, 0

            $"IPv6: {srcIp}:{srcPort} → {dstIp}:{dstPort}, next={nextHeader}, len={bytes.Length}"
    
    
    let summarizePacket (bytes: byte[]) =
        if isNull bytes then "<null packet>"
        elif bytes.Length < 20 then $"<packet too short: {bytes.Length} bytes>"
        else
            let v = int bytes[0] >>> 4
            match v with
            | 4 -> summarizeIPv4Packet bytes
            | 6 -> summarizeIPv6Packet bytes
            | _ -> $"<Unknown IP version={v}, len={bytes.Length}>"

    
    type Logger with
        static member logTracePackets (packets : byte[][], getMessage: unit -> obj, [<CallerMemberName; Optional; DefaultParameterValue("")>] ?callerName) =
            if Logger.shouldLog TraceLog then
                packets
                |> Array.map (fun e -> Logger.logTrace (fun () -> $"{getMessage()}'%A{(summarizePacket e)}'."), callerName)
                |> ignore
