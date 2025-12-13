namespace Softellect.Vpn.Core

module PacketDebug =

    /// Get IP version from packet (4 = IPv4, 6 = IPv6, 0 = empty/invalid)
    let getIpVersion (packet: byte[]) =
        if packet.Length = 0 then 0
        else int packet[0] >>> 4

    
    let inline b2u16 (a: byte) (b: byte) = (uint16 a <<< 8) ||| uint16 b

    
    let inline readUInt16BE (buf: byte[]) (offset: int) =
        b2u16 buf[offset] buf[offset + 1]

    
    let inline ipv4ToString (a: byte) (b: byte) (c: byte) (d: byte) =
        $"{a}.{b}.{c}.{d}"

    
    let inline isAsciiPrintable (b: byte) =
        b >= 32uy && b <= 126uy
    

    // let summarizePacket (bytes: byte[]) =
    //     if bytes.Length < 20 then
    //         $"<packet too short: {bytes.Length} bytes>"
    //     else
    //         let v = int bytes[0] >>> 4
    //         match v with
    //         | 4 ->
    //             // IPv4
    //             let srcIp  = ipv4ToString bytes[12] bytes[13] bytes[14] bytes[15]
    //             let dstIp  = ipv4ToString bytes[16] bytes[17] bytes[18] bytes[19]
    //             let proto  = bytes[9]
    //
    //             let srcPort, dstPort =
    //                 if proto = 6uy || proto = 17uy then
    //                     // TCP or UDP
    //                     let shp = uint16 bytes[20] <<< 8 ||| uint16 bytes[21]
    //                     let dhp = uint16 bytes[22] <<< 8 ||| uint16 bytes[23]
    //                     int shp, int dhp
    //                 else
    //                     0, 0
    //
    //             $"IPv4: {srcIp}:{srcPort} → {dstIp}:{dstPort}, proto={proto}, len={bytes.Length}"
    //
    //         | 6 ->
    //             // IPv6
    //             if bytes.Length < 40 then
    //                 $"<IPv6 packet too short: {bytes.Length} bytes>"
    //             else
    //                 let srcIp = IPAddress(bytes[8..23])
    //                 let dstIp = IPAddress(bytes[24..39])
    //                 let nextHeader = bytes[6]
    //
    //                 // Parse ports only for UDP/TCP
    //                 let srcPort, dstPort =
    //                     if nextHeader = 6uy || nextHeader = 17uy then
    //                         let offs = 40
    //                         if bytes.Length >= offs + 4 then
    //                             let shp = uint16 bytes[offs] <<< 8 ||| uint16 bytes[offs+1]
    //                             let dhp = uint16 bytes[offs+2] <<< 8 ||| uint16 bytes[offs+3]
    //                             int shp, int dhp
    //                         else 0,0
    //                     else 0,0
    //
    //                 $"IPv6: {srcIp}:{srcPort} → {dstIp}:{dstPort}, next={nextHeader}, len={bytes.Length}"
    //
    //         | _ ->
    //             $"<Unknown IP version={v}, len={bytes.Length}>"

    let summarizePacket (bytes: byte[]) =

        let tryParseDnsName (payload: byte[]) (startOffset: int) =
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

        let trySummarizeDns (udpPayload: byte[]) =
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

                        $" name={name} qtype={qtypeName} qclass={qclass}"

                if qr then
                    $"DNS r txid=0x{txid:X4} qd={qd} an={an} ns={ns} ar={ar} rcode={rcode}{namePart}"
                else
                    $"DNS q txid=0x{txid:X4} qd={qd} an={an} ns={ns} ar={ar}{namePart}"

        if bytes.Length < 20 then
            $"<packet too short: {bytes.Length} bytes>"
        else
            let v = int bytes[0] >>> 4
            match v with
            | 4 ->
                // IPv4
                let ihl = int (bytes[0] &&& 0x0Fuy) * 4
                if ihl < 20 || bytes.Length < ihl then
                    $"<IPv4 header invalid: ihl={ihl}, len={bytes.Length}>"
                else
                    let srcIp = ipv4ToString bytes[12] bytes[13] bytes[14] bytes[15]
                    let dstIp = ipv4ToString bytes[16] bytes[17] bytes[18] bytes[19]
                    let proto = bytes[9]

                    let srcPort, dstPort =
                        if (proto = 6uy || proto = 17uy) && bytes.Length >= ihl + 4 then
                            // TCP or UDP ports at start of L4 header
                            let shp = readUInt16BE bytes ihl
                            let dhp = readUInt16BE bytes (ihl + 2)
                            int shp, int dhp
                        else
                            0, 0

                    // If UDP and looks like DNS, try to decode DNS header + qname
                    if proto = 17uy && bytes.Length >= ihl + 8 then
                        let udpLen = int (readUInt16BE bytes (ihl + 4))
                        let payloadOffset = ihl + 8
                        let payloadLenByIp = bytes.Length - payloadOffset
                        let payloadLenByUdp = max 0 (udpLen - 8)
                        let payloadLen = min payloadLenByIp payloadLenByUdp

                        if payloadLen > 0 && (srcPort = 53 || dstPort = 53) then
                            let udpPayload = Array.sub bytes payloadOffset payloadLen
                            $"IPv4 UDP: {srcIp}:{srcPort} → {dstIp}:{dstPort}, len={bytes.Length} {trySummarizeDns udpPayload}"
                        else
                            $"IPv4: {srcIp}:{srcPort} → {dstIp}:{dstPort}, proto={proto}, len={bytes.Length}"
                    else
                        $"IPv4: {srcIp}:{srcPort} → {dstIp}:{dstPort}, proto={proto}, len={bytes.Length}"

            | 6 ->
                // IPv6
                if bytes.Length < 40 then
                    $"<IPv6 packet too short: {bytes.Length} bytes>"
                else
                    let srcIp = System.Net.IPAddress(bytes[8..23])
                    let dstIp = System.Net.IPAddress(bytes[24..39])
                    let nextHeader = bytes[6]

                    // Parse ports only for UDP/TCP
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

            | _ ->
                $"<Unknown IP version={v}, len={bytes.Length}>"
