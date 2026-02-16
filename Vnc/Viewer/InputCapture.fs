namespace Softellect.Vnc.Viewer

open System.Runtime.InteropServices
open System.Windows.Forms
open Softellect.Sys.Logging
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Viewer.WcfClient

module InputCapture =

    [<DllImport("user32.dll")>]
    extern uint32 MapVirtualKey(uint32 uCode, uint32 uMapType)

    /// Translates local coordinates to remote screen coordinates.
    let translateCoords (localX: int) (localY: int) (panelWidth: int) (panelHeight: int) (remoteWidth: int) (remoteHeight: int) =
        let x = int (float localX / float panelWidth * float remoteWidth)
        let y = int (float localY / float panelHeight * float remoteHeight)
        (x, y)

    /// Sends a mouse event to the remote service.
    let sendMouseEvent (client: VncWcfClient) (remoteWidth: int) (remoteHeight: int) (panelWidth: int) (panelHeight: int) (e: MouseEventArgs) (eventType: string) =
        let rx, ry = translateCoords e.X e.Y panelWidth panelHeight remoteWidth remoteHeight

        let event =
            match eventType with
            | "move" -> Some (MouseMove (rx, ry))
            | "down" ->
                let button =
                    match e.Button with
                    | MouseButtons.Right -> RightButton
                    | MouseButtons.Middle -> MiddleButton
                    | _ -> LeftButton
                Some (MouseButton (rx, ry, button, true))
            | "up" ->
                let button =
                    match e.Button with
                    | MouseButtons.Right -> RightButton
                    | MouseButtons.Middle -> MiddleButton
                    | _ -> LeftButton
                Some (MouseButton (rx, ry, button, false))
            | "wheel" ->
                Some (MouseWheel (rx, ry, e.Delta))
            | _ -> None

        match event with
        | Some evt ->
            match client.sendInput evt with
            | Ok () -> ()
            | Error e -> Logger.logTrace (fun () -> $"InputCapture: sendInput error: %A{e}")
        | None -> ()

    /// Sends a keyboard event to the remote service.
    let sendKeyEvent (client: VncWcfClient) (e: KeyEventArgs) (isDown: bool) =
        let isExtended =
            match e.KeyCode with
            | Keys.RControlKey | Keys.RMenu | Keys.Insert | Keys.Delete
            | Keys.Home | Keys.End | Keys.PageUp | Keys.PageDown
            | Keys.Up | Keys.Down | Keys.Left | Keys.Right
            | Keys.NumLock | Keys.PrintScreen | Keys.Pause -> true
            | _ -> false

        let scanCode = MapVirtualKey(uint32 e.KeyValue, 0u) |> int
        let evt = KeyPress (e.KeyValue, scanCode, isDown, isExtended)
        match client.sendInput evt with
        | Ok () -> ()
        | Error err -> Logger.logTrace (fun () -> $"InputCapture: sendKeyEvent error: %A{err}")
