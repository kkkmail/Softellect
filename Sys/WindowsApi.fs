namespace Softellect.Sys

open System.Runtime.InteropServices
open Softellect.Sys.Errors
open Softellect.Sys.Primitives
open System

module WindowsApi =

    let private toError e = e |> WindowsApiErr |> Error

    type MonitorEnumProc = delegate of IntPtr * IntPtr * IntPtr * IntPtr -> bool

    [<StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)>]
    type DEVMODE =
        val mutable dmDeviceName: string
        val mutable dmSpecVersion: int16
        val mutable dmDriverVersion: int16
        val mutable dmSize: int16
        val mutable dmDriverExtra: int16
        val mutable dmFields: int
        val mutable dmPositionX: int
        val mutable dmPositionY: int
        val mutable dmDisplayOrientation: int
        val mutable dmDisplayFixedOutput: int
        val mutable dmColor: int16
        val mutable dmDuplex: int16
        val mutable dmYResolution: int16
        val mutable dmTTOption: int16
        val mutable dmCollate: int16
        val mutable dmFormName: string
        val mutable dmLogPixels: int16
        val mutable dmBitsPerPel: int
        val mutable dmPelsWidth: int
        val mutable dmPelsHeight: int
        val mutable dmDisplayFlags: int
        val mutable dmDisplayFrequency: int

    let SM_CXSCREEN = 0 // Primary monitor width
    let SM_CYSCREEN = 1 // Primary monitor height
    let BITSPIXEL = 12   // Color depth
    let LOGPIXELSX = 88  // DPI (horizontal)
    let LOGPIXELSY = 90  // DPI (vertical)


    [<DllImport("user32.dll")>]
    extern int GetSystemMetrics(int nIndex)

    [<DllImport("gdi32.dll")>]
    extern int GetDeviceCaps(IntPtr hdc, int nIndex)

    [<DllImport("user32.dll")>]
    extern IntPtr GetDC(IntPtr hWnd)

    [<DllImport("user32.dll")>]
    extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC)

    [<DllImport("user32.dll")>]
    extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData)

    // [<DllImport("user32.dll")>]
    // extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode)

    let tryGetMonitorResolution () =
        try
            let width =  GetSystemMetrics(SM_CXSCREEN)
            let height = GetSystemMetrics(SM_CYSCREEN)
            Ok { monitorWidth = width; monitorHeight = height }
        with
        | e -> e |> WindowsApiExn |> toError


    let tryGetColorDepth () =
        try
            let hdc = GetDC(IntPtr.Zero)
            let colorDepth = GetDeviceCaps(hdc, BITSPIXEL)
            ReleaseDC(IntPtr.Zero, hdc) |> ignore
            colorDepth |> ColorDepth |> Ok
        with
        | e -> e |> WindowsApiExn |> toError


    let tryGetDpi () =
        try
            let hdc = GetDC(IntPtr.Zero)
            let dpiX = GetDeviceCaps(hdc, LOGPIXELSX)
            let dpiY = GetDeviceCaps(hdc, LOGPIXELSY)
            ReleaseDC(IntPtr.Zero, hdc) |> ignore
            { dpiX = dpiX; dpiY = dpiY } |> Ok
        with
        | e -> e |> WindowsApiExn |> toError
