namespace Softellect.Vpn.Core

open System.Net

module PacketDebug =

    /// Get IP version from packet (4 = IPv4, 6 = IPv6, 0 = empty/invalid)
    let getIpVersion (packet: byte[]) =
        if packet.Length = 0 then 0
        else int packet[0] >>> 4

    let ipv4ToString (b0: byte) (b1: byte) (b2: byte) (b3: byte) =
        $"{int b0}.{int b1}.{int b2}.{int b3}"


    let summarizePacket (bytes: byte[]) =
        if bytes.Length < 20 then
            $"<packet too short: {bytes.Length} bytes>"
        else
            let v = int bytes[0] >>> 4
            match v with
            | 4 ->
                // IPv4
                let srcIp  = ipv4ToString bytes[12] bytes[13] bytes[14] bytes[15]
                let dstIp  = ipv4ToString bytes[16] bytes[17] bytes[18] bytes[19]
                let proto  = bytes[9]

                let srcPort, dstPort =
                    if proto = 6uy || proto = 17uy then
                        // TCP or UDP
                        let shp = uint16 bytes[20] <<< 8 ||| uint16 bytes[21]
                        let dhp = uint16 bytes[22] <<< 8 ||| uint16 bytes[23]
                        int shp, int dhp
                    else
                        0, 0

                $"IPv4: {srcIp}:{srcPort} → {dstIp}:{dstPort}, proto={proto}, len={bytes.Length}"

            | 6 ->
                // IPv6
                if bytes.Length < 40 then
                    $"<IPv6 packet too short: {bytes.Length} bytes>"
                else
                    let srcIp = IPAddress(bytes[8..23])
                    let dstIp = IPAddress(bytes[24..39])
                    let nextHeader = bytes[6]

                    // Parse ports only for UDP/TCP
                    let srcPort, dstPort =
                        if nextHeader = 6uy || nextHeader = 17uy then
                            let offs = 40
                            if bytes.Length >= offs + 4 then
                                let shp = uint16 bytes[offs] <<< 8 ||| uint16 bytes[offs+1]
                                let dhp = uint16 bytes[offs+2] <<< 8 ||| uint16 bytes[offs+3]
                                int shp, int dhp
                            else 0,0
                        else 0,0

                    $"IPv6: {srcIp}:{srcPort} → {dstIp}:{dstPort}, next={nextHeader}, len={bytes.Length}"

            | _ ->
                $"<Unknown IP version={v}, len={bytes.Length}>"
