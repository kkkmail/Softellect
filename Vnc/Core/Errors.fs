namespace Softellect.Vnc.Core

open Softellect.Sys.Errors
open Softellect.Wcf.Errors
open Softellect.Vnc.Core.Primitives

module Errors =
    type VncWcfError =
        | VncAuthWcfErr of WcfError
        | VncControlWcfErr of WcfError
        | VncFileTransferWcfErr of WcfError
        | VncRepeaterWcfErr of WcfError

    type VncCaptureError =
        | DxgiInitErr of string
        | FrameCaptureErr of string
        | EncodingErr of string

    type VncInputError =
        | SendInputErr of string
        | ClipboardErr of string

    type VncConnectionError =
        | RepeaterUnreachableErr of string
        | MachineOfflineErr of VncMachineName
        | AuthFailedErr of string
        | SessionExpiredErr of VncSessionId

    type VncFileTransferError =
        | DirectoryListErr of string
        | FileReadErr of string
        | FileWriteErr of string
        | TransferCancelledErr

    type VncError =
        | VncAggregateErr of VncError * List<VncError>
        | VncCaptureErr of VncCaptureError
        | VncInputErr of VncInputError
        | VncConnectionErr of VncConnectionError
        | VncFileTransferErr of VncFileTransferError
        | VncWcfErr of VncWcfError
        | VncConfigErr of string
        | VncCryptoErr of CryptoError
        | VncGeneralErr of string

        static member addError a b =
            match a, b with
            | VncAggregateErr (x, w), VncAggregateErr (y, z) -> VncAggregateErr (x, w @ (y :: z))
            | VncAggregateErr (x, w), _ -> VncAggregateErr (x, w @ [b])
            | _, VncAggregateErr (y, z) -> VncAggregateErr (a, y :: z)
            | _ -> VncAggregateErr (a, [b])

        static member (+) (a, b) = VncError.addError a b
        member a.add b = a + b

    type VncResult<'T> = Result<'T, VncError>
    type VncUnitResult = Result<unit, VncError>
