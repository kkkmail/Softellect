using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.FSharp.Core;
using static Softellect.Vnc.Core.Primitives;

// ReSharper disable once CheckNamespace
namespace Softellect.Vnc.Interop;

/// <summary>
/// Clipboard access using Win32 APIs (STA thread required for some operations).
/// </summary>
public static class ClipboardInterop
{
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>
    /// Gets the current clipboard content.
    /// Must be called from a thread that can access the clipboard.
    /// </summary>
    public static FSharpResult<ClipboardData, string> GetClipboardContent()
    {
        try
        {
            ClipboardData? result = null;

            RunOnStaThread(() =>
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    return;
                }

                try
                {
                    if (IsClipboardFormatAvailable(CF_UNICODETEXT))
                    {
                        var hData = GetClipboardData(CF_UNICODETEXT);
                        if (hData != IntPtr.Zero)
                        {
                            var pData = GlobalLock(hData);
                            if (pData != IntPtr.Zero)
                            {
                                try
                                {
                                    var text = Marshal.PtrToStringUni(pData);
                                    if (text != null)
                                    {
                                        result = ClipboardData.NewTextClip(text);
                                    }
                                }
                                finally
                                {
                                    GlobalUnlock(hData);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            });

            return result != null
                ? FSharpResult<ClipboardData, string>.NewOk(result)
                : FSharpResult<ClipboardData, string>.NewError("No supported clipboard content");
        }
        catch (Exception ex)
        {
            return FSharpResult<ClipboardData, string>.NewError($"GetClipboardContent failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the clipboard content.
    /// Must be called from a thread that can access the clipboard.
    /// </summary>
    public static FSharpResult<Unit, string> SetClipboardContent(ClipboardData data)
    {
        try
        {
            string? error = null;

            RunOnStaThread(() =>
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    error = "Failed to open clipboard";
                    return;
                }

                try
                {
                    EmptyClipboard();

                    if (data.IsTextClip)
                    {
                        var text = ((ClipboardData.TextClip)data).Item;
                        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                        var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);

                        if (hGlobal == IntPtr.Zero)
                        {
                            error = "GlobalAlloc failed";
                            return;
                        }

                        var pGlobal = GlobalLock(hGlobal);
                        if (pGlobal == IntPtr.Zero)
                        {
                            error = "GlobalLock failed";
                            return;
                        }

                        try
                        {
                            Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
                        }
                        finally
                        {
                            GlobalUnlock(hGlobal);
                        }

                        SetClipboardData(CF_UNICODETEXT, hGlobal);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            });

            return error != null
                ? FSharpResult<Unit, string>.NewError(error)
                : FSharpResult<Unit, string>.NewOk(default!);
        }
        catch (Exception ex)
        {
            return FSharpResult<Unit, string>.NewError($"SetClipboardContent failed: {ex.Message}");
        }
    }

    private static void RunOnStaThread(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
        }
        else
        {
            Exception? threadException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadException != null)
                throw threadException;
        }
    }
}
