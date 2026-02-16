using System;
using System.Runtime.InteropServices;
using Microsoft.FSharp.Core;

// ReSharper disable once CheckNamespace
namespace Softellect.Vnc.Interop;

/// <summary>
/// Injects mouse and keyboard input using Win32 SendInput API.
/// </summary>
public static class InputInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUT_UNION union;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    /// <summary>
    /// Sends a mouse event at the specified screen coordinates.
    /// </summary>
    /// <param name="x">Screen X coordinate.</param>
    /// <param name="y">Screen Y coordinate.</param>
    /// <param name="buttons">Button flags (1=left, 2=right, 4=middle).</param>
    /// <param name="isDown">True for button down, false for button up.</param>
    /// <param name="wheel">Mouse wheel delta.</param>
    /// <returns>Result with Unit or error message.</returns>
    public static FSharpResult<Unit, string> SendMouseEvent(int x, int y, int buttons, bool isDown, int wheel)
    {
        try
        {
            var screenWidth = GetSystemMetrics(SM_CXSCREEN);
            var screenHeight = GetSystemMetrics(SM_CYSCREEN);

            if (screenWidth == 0 || screenHeight == 0)
                return FSharpResult<Unit, string>.NewError("Failed to get screen metrics");

            // Convert to absolute coordinates (0-65535 range)
            var absX = (int)((x * 65535.0) / screenWidth);
            var absY = (int)((y * 65535.0) / screenHeight);

            uint flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

            if (buttons == 1) flags |= isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
            if (buttons == 2) flags |= isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
            if (buttons == 4) flags |= isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
            if (wheel != 0) flags |= MOUSEEVENTF_WHEEL;

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                union = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        mouseData = wheel,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var result = SendInput(1, [input], Marshal.SizeOf<INPUT>());
            if (result == 0)
            {
                var error = Marshal.GetLastWin32Error();
                return FSharpResult<Unit, string>.NewError($"SendInput failed with error {error}");
            }

            return FSharpResult<Unit, string>.NewOk(default!);
        }
        catch (Exception ex)
        {
            return FSharpResult<Unit, string>.NewError($"SendMouseEvent failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a keyboard event.
    /// </summary>
    /// <param name="virtualKey">Virtual key code.</param>
    /// <param name="scanCode">Scan code.</param>
    /// <param name="isKeyUp">True for key up, false for key down.</param>
    /// <param name="isExtended">True for extended key (e.g., right Ctrl, right Alt, arrows).</param>
    /// <returns>Result with Unit or error message.</returns>
    public static FSharpResult<Unit, string> SendKeyboardEvent(int virtualKey, int scanCode, bool isKeyUp, bool isExtended)
    {
        try
        {
            uint flags = KEYEVENTF_SCANCODE;
            if (isKeyUp) flags |= KEYEVENTF_KEYUP;
            if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                union = new INPUT_UNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)virtualKey,
                        wScan = (ushort)scanCode,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var result = SendInput(1, [input], Marshal.SizeOf<INPUT>());
            if (result == 0)
            {
                var error = Marshal.GetLastWin32Error();
                return FSharpResult<Unit, string>.NewError($"SendInput failed with error {error}");
            }

            return FSharpResult<Unit, string>.NewOk(default!);
        }
        catch (Exception ex)
        {
            return FSharpResult<Unit, string>.NewError($"SendKeyboardEvent failed: {ex.Message}");
        }
    }
}
