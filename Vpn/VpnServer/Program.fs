namespace Softellect.Vpn.VpnServer

open Softellect.Vpn.Server.Program

module Program =

    [<EntryPoint>]
    let main args = vpnServerMain "VpnServer" args
