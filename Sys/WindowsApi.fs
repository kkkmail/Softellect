namespace Softellect.Sys

open System.Runtime.InteropServices
open Softellect.Sys.Errors
open Softellect.Sys.Logging
open Softellect.Sys.Primitives
open System

module WindowsApi =

    let private toError e = e |> WindowsApiErr |> Error

    type MonitorEnumProc = delegate of IntPtr * IntPtr * IntPtr * IntPtr -> bool

    // [<StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)>]
    // type DEVMODE =
    //     val mutable dmDeviceName: string
    //     val mutable dmSpecVersion: int16
    //     val mutable dmDriverVersion: int16
    //     val mutable dmSize: int16
    //     val mutable dmDriverExtra: int16
    //     val mutable dmFields: int
    //     val mutable dmPositionX: int
    //     val mutable dmPositionY: int
    //     val mutable dmDisplayOrientation: int
    //     val mutable dmDisplayFixedOutput: int
    //     val mutable dmColor: int16
    //     val mutable dmDuplex: int16
    //     val mutable dmYResolution: int16
    //     val mutable dmTTOption: int16
    //     val mutable dmCollate: int16
    //     val mutable dmFormName: string
    //     val mutable dmLogPixels: int16
    //     val mutable dmBitsPerPel: int
    //     val mutable dmPelsWidth: int
    //     val mutable dmPelsHeight: int
    //     val mutable dmDisplayFlags: int
    //     val mutable dmDisplayFrequency: int

    [<StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)>]
    type DEVMODE =
        [<MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)>]
        val mutable dmDeviceName: string // C fixed-size array: char[32]

        val mutable dmSpecVersion: int16 // WORD (16 bits)
        val mutable dmDriverVersion: int16 // WORD (16 bits)
        val mutable dmSize: int16 // WORD (16 bits)
        val mutable dmDriverExtra: int16 // WORD (16 bits)
        val mutable dmFields: int32 // DWORD (32 bits)
        val mutable dmPositionX: int32 // LONG (32 bits)
        val mutable dmPositionY: int32 // LONG (32 bits)
        val mutable dmDisplayOrientation: int32 // DWORD (32 bits)
        val mutable dmDisplayFixedOutput: int32 // DWORD (32 bits)
        val mutable dmColor: int16 // WORD (16 bits)
        val mutable dmDuplex: int16 // WORD (16 bits)
        val mutable dmYResolution: int16 // WORD (16 bits)
        val mutable dmTTOption: int16 // WORD (16 bits)
        val mutable dmCollate: int16 // WORD (16 bits)
        val mutable dmFormName: string // C fixed-size array: char[32]
        val mutable dmLogPixels: int16 // WORD (16 bits)
        val mutable dmBitsPerPel: int32 // DWORD (32 bits)
        val mutable dmPelsWidth: int32 // DWORD (32 bits)
        val mutable dmPelsHeight: int32 // DWORD (32 bits)
        val mutable dmDisplayFlags: int32 // DWORD (32 bits)
        val mutable dmDisplayFrequency: int32 // DWORD (32 bits)


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

    [<DllImport("user32.dll", CharSet = CharSet.Ansi)>]
    extern int ChangeDisplaySettingsEx(string lpszDeviceName, DEVMODE& lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam)

    let CDS_UPDATEREGISTRY = 0x00000001
    let CDS_TEST = 0x00000002
    let DISP_CHANGE_SUCCESSFUL = 0


    let tryChangeResolution (mr : MonitorResolution) =
        try
            let mutable devMode = Activator.CreateInstance<DEVMODE>()
            devMode.dmSize <- int16 (Marshal.SizeOf<DEVMODE>())
            devMode.dmPelsWidth <- mr.monitorWidth
            devMode.dmPelsHeight <- mr.monitorHeight
            devMode.dmFields <- 0x180000 // DM_PELSWIDTH | DM_PELSHEIGHT

            let result = ChangeDisplaySettingsEx(null, &devMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero)

            if result = DISP_CHANGE_SUCCESSFUL then
                Logger.logInfo $"Resolution changed to %A{mr}"
                Ok()
            else
                let m = $"Failed to change resolution to %A{mr}. Error code: {result}."
                Logger.logError m
                m |> WindowsApiCallErr |> toError
        with
        | e -> e |> WindowsApiExn |> toError


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
