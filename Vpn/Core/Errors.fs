namespace Softellect.Vpn.Core

open Softellect.Sys.Primitives
open Softellect.Wcf.Errors
open Softellect.Vpn.Core.Primitives

module Errors =

    type VpnWcfError =
        | AuthWcfErr of WcfError
        | SendPacketWcfErr of WcfError
        | ReceivePacketsWcfErr of WcfError


    type VpnAuthError =
        | InvalidClientIdErr of VpnClientId
        | ClientNotFoundErr of VpnClientId
        | InvalidSignatureErr
        | AuthExpiredErr
        | KeyNotFoundErr of KeyId
        | AuthWcfError of VpnWcfError
        | AuthCryptoErr
        | NoAvailableSessionsErr


    type VpnTunnelError =
        | WinTunCreateAdapterErr of string
        | WinTunStartSessionErr of string
        | WinTunReadPacketErr of string
        | WinTunWritePacketErr of string
        | WinTunCloseErr of string


    type VpnKillSwitchError =
        | WfpEngineOpenErr of string
        | WfpAddFilterErr of string
        | WfpRemoveFilterErr of string
        | WfpEngineCloseErr of string


    type VpnConnectionError =
        | ServerUnreachableErr of string
        | ConnectionTimeoutErr
        | AuthFailedErr of VpnAuthError
        | TunnelErr of VpnTunnelError
        | KillSwitchErr of VpnKillSwitchError


    type VpnServerError =
        | ClientAuthErr of VpnAuthError
        | PacketForwardErr of string
        | SessionExpiredErr of VpnSessionId
        | ClientNotRegisteredErr of VpnClientId


    type VpnError =
        | VpnAggregateErr of VpnError  * List<VpnError>
        | ConnectionErr of VpnConnectionError
        | ServerErr of VpnServerError
        | ConfigErr of string
        | CryptoErr of Softellect.Sys.Errors.CryptoError

        static member addError a b =

            match a, b with
            | VpnAggregateErr (x, w), VpnAggregateErr (y, z) -> VpnAggregateErr (x, w @ (y :: z))
            | VpnAggregateErr (x, w), _ -> VpnAggregateErr (x, w @ [b])
            | _, VpnAggregateErr (y, z) -> VpnAggregateErr (a, y :: z)
            | _ -> VpnAggregateErr (a, [b])

        static member (+) (a, b) = VpnError.addError a b
        member a.add b = a + b


    type VpnResult<'T> = Result<'T, VpnError>
    type VpnUnitResult = Result<unit, VpnError>
