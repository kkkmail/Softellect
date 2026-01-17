namespace Softellect.Vpn.VpnServerLinux

open Softellect.Vpn.Server.Program

module Program =

    /// To run:
    ///     ASPNETCORE_URLS=http://0.0.0.0:<server_port> dotnet VpnServerLinux.dll > vpn.log 2>&1
    [<EntryPoint>]
    let main args = vpnServerMain args
