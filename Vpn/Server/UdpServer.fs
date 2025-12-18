namespace Softellect.Vpn.Server

open Softellect.Vpn.Core.Errors
open Softellect.Vpn.Core.ServiceInfo

module UdpServer =

    type VpnUdpService(service: IVpnService) =

        interface IVpnWcfService with
            member _.authenticate data =
                failwith "IVpnWcfService.authenticate is not implemented yet by VpnUdpService."

            member _.sendPackets data =
                failwith "IVpnWcfService.sendPackets is not implemented yet by VpnUdpService."

            member _.receivePackets data =
                failwith "IVpnWcfService.sendPackets is not implemented yet by VpnUdpService."


    let getUdpProgram (data : VpnServerData) getService argv =
        failwith "getUdpProgram is not implemented yet."
