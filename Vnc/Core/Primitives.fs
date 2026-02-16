namespace Softellect.Vnc.Core

open System
open Softellect.Sys.Primitives
open Softellect.Sys.AppSettings

module Primitives =

    /// Size of viewerId prefix in encrypted auth messages (Guid = 16 bytes).
    [<Literal>]
    let VncClientIdPrefixSize = 16
    [<Literal>]
    let VncServiceName = "VncService"

    [<Literal>]
    let VncRepeaterServiceName = "VncRepeaterService"

    [<Literal>]
    let VncAdminServiceName = "VncAdminService"

    type VncViewerId =
        | VncViewerId of Guid
        member this.value = let (VncViewerId v) = this in v
        static member tryCreate (s: string) =
            match Guid.TryParse s with
            | true, g -> Some (VncViewerId g)
            | false, _ -> None
        static member create() = Guid.NewGuid() |> VncViewerId


    type VncMachineName =
        | VncMachineName of string
        member this.value = let (VncMachineName v) = this in v

    type VncMachineId =
        | VncMachineId of Guid
        member this.value = let (VncMachineId v) = this in v
        static member tryCreate (s: string) =
            match Guid.TryParse s with
            | true, g -> Some (VncMachineId g)
            | false, _ -> None
        static member create() = Guid.NewGuid() |> VncMachineId

    type VncSessionId =
        | VncSessionId of Guid
        member this.value = let (VncSessionId v) = this in v
        static member create() = Guid.NewGuid() |> VncSessionId

    type VncMachineStatus =
        | Online
        | Offline
        | Unknown

    type VncMachineInfo =
        {
            machineName : VncMachineName
            machineId : VncMachineId
            status : VncMachineStatus
        }

    type MouseButton =
        | LeftButton
        | RightButton
        | MiddleButton

    type FrameRegion =
        {
            x : int
            y : int
            width : int
            height : int
            data : byte[]
        }

    type MoveRegion =
        {
            x : int
            y : int
            width : int
            height : int
            sourceX : int
            sourceY : int
        }

    type FrameUpdate =
        {
            sequenceNumber : uint64
            screenWidth : int
            screenHeight : int
            regions : FrameRegion[]
            moveRegions : MoveRegion[]
            cursorX : int
            cursorY : int
            cursorShape : byte[] option
        }

    type InputEvent =
        | MouseMove of x: int * y: int
        | MouseButton of x: int * y: int * button: MouseButton * isDown: bool
        | MouseWheel of x: int * y: int * delta: int
        | KeyPress of virtualKey: int * scanCode: int * isDown: bool * isExtended: bool

    type ClipboardData =
        | TextClip of string
        | FileListClip of string[]

    [<Literal>]
    let DefaultVncWcfPort = 5090

    [<Literal>]
    let DefaultVncUdpPort = 5091

    type VncConnectRequest =
        {
            viewerId : VncViewerId
            viewerUdpAddress : string
            viewerUdpPort : int
            timestamp : DateTime
        }

    type VncConnectResponse =
        {
            sessionId : VncSessionId
            screenWidth : int
            screenHeight : int
            sessionAesKey : byte[]
        }
