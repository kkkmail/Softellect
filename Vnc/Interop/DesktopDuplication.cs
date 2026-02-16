using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.FSharp.Core;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

// ReSharper disable once CheckNamespace
namespace Softellect.Vnc.Interop;

/// <summary>
/// Frame data captured from DXGI Desktop Duplication.
/// </summary>
public sealed class FrameData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Stride { get; init; }
    public byte[] PixelData { get; init; } = Array.Empty<byte>();
    public Rectangle[] DirtyRects { get; init; } = Array.Empty<Rectangle>();
    public VncMoveRect[] MoveRects { get; init; } = Array.Empty<VncMoveRect>();
    public Point CursorPosition { get; init; }
    public byte[]? CursorShape { get; init; }
    public bool CursorVisible { get; init; }
}

/// <summary>
/// Move rectangle information.
/// </summary>
public struct VncMoveRect
{
    public Point SourcePoint;
    public Rectangle DestinationRect;
}

/// <summary>
/// DXGI Desktop Duplication screen capture using Vortice.DirectX.
/// </summary>
public sealed class DesktopDuplication : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _stagingTexture;
    private int _width;
    private int _height;
    private bool _disposed;
    private readonly int _outputIndex;

    private DesktopDuplication(
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGIOutputDuplication duplication,
        ID3D11Texture2D stagingTexture,
        int width,
        int height,
        int outputIndex)
    {
        _device = device;
        _context = context;
        _duplication = duplication;
        _stagingTexture = stagingTexture;
        _width = width;
        _height = height;
        _outputIndex = outputIndex;
    }

    /// <summary>
    /// Creates a new DesktopDuplication instance for the specified output (monitor).
    /// </summary>
    /// <param name="outputIndex">Monitor index (0 = primary).</param>
    /// <returns>Result containing the DesktopDuplication or error message.</returns>
    public static FSharpResult<DesktopDuplication, string> Create(int outputIndex = 0)
    {
        try
        {
            ID3D11Device? device = null;
            ID3D11DeviceContext? context = null;
            var featureLevels = new FeatureLevel[] { FeatureLevel.Level_11_0 };
            var result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out device,
                out context);

            if (result.Failure || device == null || context == null)
            {
                return FSharpResult<DesktopDuplication, string>.NewError(
                    $"Failed to create D3D11 device: {result}");
            }

            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            var adapter = dxgiDevice.GetAdapter();

            var adapter1 = adapter.QueryInterface<IDXGIAdapter1>();
            adapter.Dispose();

            IDXGIOutput? output = null;
            try
            {
                adapter1.EnumOutputs((uint)outputIndex, out output);
            }
            catch
            {
                // ignored
            }

            adapter1.Dispose();

            if (output == null)
            {
                device.Dispose();
                context.Dispose();
                return FSharpResult<DesktopDuplication, string>.NewError(
                    $"Failed to get output {outputIndex}");
            }

            var output1 = output.QueryInterface<IDXGIOutput1>();
            output.Dispose();

            var desc = output1.Description;
            var width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
            var height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

            IDXGIOutputDuplication duplication;
            try
            {
                duplication = output1.DuplicateOutput(device);
            }
            catch (Exception ex)
            {
                output1.Dispose();
                device.Dispose();
                context.Dispose();
                return FSharpResult<DesktopDuplication, string>.NewError(
                    $"Failed to duplicate output: {ex.Message}");
            }

            output1.Dispose();

            var stagingDesc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None
            };

            var stagingTexture = device.CreateTexture2D(stagingDesc);

            return FSharpResult<DesktopDuplication, string>.NewOk(
                new DesktopDuplication(device, context, duplication, stagingTexture, width, height, outputIndex));
        }
        catch (Exception ex)
        {
            return FSharpResult<DesktopDuplication, string>.NewError(
                $"DesktopDuplication init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Captures a single frame from the desktop.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for a new frame.</param>
    /// <returns>Result containing FrameData or error message.</returns>
    public FSharpResult<FrameData, string> CaptureFrame(int timeoutMs = 100)
    {
        if (_disposed || _duplication == null || _context == null || _stagingTexture == null)
            return FSharpResult<FrameData, string>.NewError("DesktopDuplication is disposed");

        try
        {
            var result = _duplication.AcquireNextFrame((uint)timeoutMs, out var frameInfo, out var desktopResource);

            if (result.Failure)
            {
                if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                    return FSharpResult<FrameData, string>.NewError("timeout");

                return FSharpResult<FrameData, string>.NewError($"AcquireNextFrame failed: {result}");
            }

            try
            {
                using var texture = desktopResource!.QueryInterface<ID3D11Texture2D>();
                _context.CopyResource(_stagingTexture, texture);

                // Get dirty and move rects
                var dirtyRects = GetDirtyRects();
                var moveRects = GetMoveRects();

                // Get cursor info
                var cursorPosition = new Point(frameInfo.PointerPosition.Position.X, frameInfo.PointerPosition.Position.Y);
                var cursorVisible = frameInfo.PointerPosition.Visible;
                byte[]? cursorShape = null;

                if (frameInfo.PointerShapeBufferSize > 0)
                {
                    cursorShape = GetCursorShape(frameInfo.PointerShapeBufferSize);
                }

                // Map and read pixels
                var mapped = _context.Map(_stagingTexture, 0, MapMode.Read);
                try
                {
                    var stride = (int)mapped.RowPitch;
                    var dataSize = _height * stride;
                    var pixelData = new byte[dataSize];
                    unsafe
                    {
                        var srcPtr = (byte*)mapped.DataPointer;
                        for (var row = 0; row < _height; row++)
                        {
                            Marshal.Copy((IntPtr)(srcPtr + row * stride), pixelData, row * stride, stride);
                        }
                    }

                    return FSharpResult<FrameData, string>.NewOk(new FrameData
                    {
                        Width = _width,
                        Height = _height,
                        Stride = stride,
                        PixelData = pixelData,
                        DirtyRects = dirtyRects,
                        MoveRects = moveRects,
                        CursorPosition = cursorPosition,
                        CursorShape = cursorShape,
                        CursorVisible = cursorVisible
                    });
                }
                finally
                {
                    _context.Unmap(_stagingTexture, 0);
                }
            }
            finally
            {
                desktopResource?.Dispose();
                _duplication.ReleaseFrame();
            }
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (ex.HResult == unchecked((int)0x887A0026))
        {
            // DXGI_ERROR_ACCESS_LOST - need to reinitialize
            return FSharpResult<FrameData, string>.NewError("access_lost");
        }
        catch (Exception ex)
        {
            return FSharpResult<FrameData, string>.NewError($"CaptureFrame failed: {ex.Message}");
        }
    }

    private Rectangle[] GetDirtyRects()
    {
        try
        {
            if (_duplication == null) return Array.Empty<Rectangle>();

            var rects = new RawRect[64];
            var hr = _duplication.GetFrameDirtyRects((uint)(rects.Length * Marshal.SizeOf<RawRect>()), rects, out var rectsCount);
            if (hr.Failure) return Array.Empty<Rectangle>();

            var count = (int)rectsCount;
            var resultArr = new Rectangle[count];
            for (var i = 0; i < count; i++)
            {
                var r = rects[i];
                resultArr[i] = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            return resultArr;
        }
        catch
        {
            return Array.Empty<Rectangle>();
        }
    }

    private VncMoveRect[] GetMoveRects()
    {
        try
        {
            if (_duplication == null) return Array.Empty<VncMoveRect>();

            var rects = new Vortice.DXGI.OutduplMoveRect[64];
            var hr = _duplication.GetFrameMoveRects((uint)(rects.Length * Marshal.SizeOf<Vortice.DXGI.OutduplMoveRect>()), rects, out var rectsCount);
            if (hr.Failure) return Array.Empty<VncMoveRect>();

            var count = (int)rectsCount;
            var resultArr = new VncMoveRect[count];
            for (var i = 0; i < count; i++)
            {
                resultArr[i] = new VncMoveRect
                {
                    SourcePoint = new Point(rects[i].SourcePoint.X, rects[i].SourcePoint.Y),
                    DestinationRect = new Rectangle(
                        rects[i].DestinationRect.Left,
                        rects[i].DestinationRect.Top,
                        rects[i].DestinationRect.Right - rects[i].DestinationRect.Left,
                        rects[i].DestinationRect.Bottom - rects[i].DestinationRect.Top)
                };
            }
            return resultArr;
        }
        catch
        {
            return Array.Empty<VncMoveRect>();
        }
    }

    private byte[]? GetCursorShape(uint bufferSize)
    {
        try
        {
            if (_duplication == null) return null;

            var buffer = new byte[bufferSize];
            unsafe
            {
                fixed (byte* pBuffer = buffer)
                {
                    var hr = _duplication.GetFramePointerShape(bufferSize, (IntPtr)pBuffer, out _, out _);
                    if (hr.Failure) return null;
                }
            }
            return buffer;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reinitializes the duplication after access_lost error.
    /// </summary>
    public FSharpResult<Unit, string> Reinitialize()
    {
        Cleanup();
        var result = Create(_outputIndex);

        if (result.IsOk)
        {
            var newInstance = result.ResultValue;
            _device = newInstance._device;
            _context = newInstance._context;
            _duplication = newInstance._duplication;
            _stagingTexture = newInstance._stagingTexture;
            _width = newInstance._width;
            _height = newInstance._height;

            // Prevent the temporary from disposing our resources
            newInstance._device = null;
            newInstance._context = null;
            newInstance._duplication = null;
            newInstance._stagingTexture = null;

            return FSharpResult<Unit, string>.NewOk(default!);
        }

        return FSharpResult<Unit, string>.NewError(result.ErrorValue);
    }

    public int Width => _width;
    public int Height => _height;

    private void Cleanup()
    {
        _duplication?.Dispose();
        _duplication = null;
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Cleanup();
        }
    }
}
