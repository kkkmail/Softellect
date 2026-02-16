namespace Softellect.Vnc.Service

open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors
open Softellect.Vnc.Interop

module InputService =

    /// Processes an input event from the viewer by calling Win32 SendInput.
    let processInputEvent (event: InputEvent) : VncUnitResult =
        match event with
        | MouseMove (x, y) ->
            match InputInjector.SendMouseEvent(x, y, 0, false, 0) with
            | Ok _ -> Ok ()
            | Error e -> Error (VncInputErr (SendInputErr e))

        | MouseButton (x, y, button, isDown) ->
            let buttonFlags =
                match button with
                | LeftButton -> 1
                | RightButton -> 2
                | MiddleButton -> 4
            match InputInjector.SendMouseEvent(x, y, buttonFlags, isDown, 0) with
            | Ok _ -> Ok ()
            | Error e -> Error (VncInputErr (SendInputErr e))

        | MouseWheel (x, y, delta) ->
            match InputInjector.SendMouseEvent(x, y, 0, false, delta) with
            | Ok _ -> Ok ()
            | Error e -> Error (VncInputErr (SendInputErr e))

        | KeyPress (virtualKey, scanCode, isDown, isExtended) ->
            match InputInjector.SendKeyboardEvent(virtualKey, scanCode, not isDown, isExtended) with
            | Ok _ -> Ok ()
            | Error e -> Error (VncInputErr (SendInputErr e))
