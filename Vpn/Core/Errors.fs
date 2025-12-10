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
        | SessionExpiredErr of VpnClientId


    type VpnError =
        | ConnectionErr of VpnConnectionError
        | ServerErr of VpnServerError
        | ConfigErr of string
        | CryptoErr of Softellect.Sys.Errors.CryptoError


    type VpnResult<'T> = Result<'T, VpnError>
    type VpnUnitResult = Result<unit, VpnError>
