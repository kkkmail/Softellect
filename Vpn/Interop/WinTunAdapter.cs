using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace Softellect.Vpn.Interop;

/// <summary>
/// Managed wrapper for WinTun adapter operations.
/// </summary>
public sealed class WinTunAdapter : IDisposable
{
    private IntPtr _adapter;
    private IntPtr _session;
    private readonly string _name;
    private bool _disposed;
    private ulong _adapterLuid;

    public string Name => _name;
    public bool IsSessionActive => _session != IntPtr.Zero;
    public ulong AdapterLuid => _adapterLuid;

    private WinTunAdapter(IntPtr adapter, string name)
    {
        _adapter = adapter;
        _name = name;
        WinTun.WintunGetAdapterLUID(adapter, out _adapterLuid);
    }

    /// <summary>
    /// Creates a new WinTun adapter.
    /// </summary>
    /// <param name="name">Adapter name.</param>
    /// <param name="tunnelType">Tunnel type identifier.</param>
    /// <param name="guid">Optional GUID for the adapter.</param>
    /// <returns>Result containing the adapter or error message.</returns>
    public static Result<WinTunAdapter> Create(string name, string tunnelType, Guid? guid = null)
    {
        var adapterGuid = guid ?? Guid.NewGuid();
        var adapter = WinTun.WintunCreateAdapter(name, tunnelType, ref adapterGuid);

        if (adapter == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return Result<WinTunAdapter>.Failure($"Failed to create WinTun adapter: {new Win32Exception(error).Message} (Error: {error})");
        }

        return Result<WinTunAdapter>.Success(new WinTunAdapter(adapter, name));
    }

    /// <summary>
    /// Opens an existing WinTun adapter.
    /// </summary>
    /// <param name="name">Adapter name.</param>
    /// <returns>Result containing the adapter or error message.</returns>
    public static Result<WinTunAdapter> Open(string name)
    {
        var adapter = WinTun.WintunOpenAdapter(name);

        if (adapter == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return Result<WinTunAdapter>.Failure($"Failed to open WinTun adapter: {new Win32Exception(error).Message} (Error: {error})");
        }

        return Result<WinTunAdapter>.Success(new WinTunAdapter(adapter, name));
    }

    /// <summary>
    /// Starts a packet session on this adapter.
    /// </summary>
    /// <param name="ringCapacity">Ring buffer capacity (default: 4MB).</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Unit> StartSession(uint ringCapacity = 0x400000)
    {
        if (_session != IntPtr.Zero)
        {
            return Result<Unit>.Failure("Session already active");
        }

        if (ringCapacity < WinTun.MinRingCapacity || ringCapacity > WinTun.MaxRingCapacity)
        {
            return Result<Unit>.Failure($"Ring capacity must be between {WinTun.MinRingCapacity} and {WinTun.MaxRingCapacity}");
        }

        _session = WinTun.WintunStartSession(_adapter, ringCapacity);

        if (_session == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return Result<Unit>.Failure($"Failed to start session: {new Win32Exception(error).Message} (Error: {error})");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>
    /// Ends the current packet session.
    /// </summary>
    public void EndSession()
    {
        if (_session != IntPtr.Zero)
        {
            WinTun.WintunEndSession(_session);
            _session = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets the wait event handle for incoming packets.
    /// </summary>
    /// <returns>Event handle or IntPtr.Zero if no session.</returns>
    public IntPtr GetReadWaitEvent()
    {
        return _session != IntPtr.Zero ? WinTun.WintunGetReadWaitEvent(_session) : IntPtr.Zero;
    }

    /// <summary>
    /// Receives a packet from the adapter.
    /// </summary>
    /// <returns>Packet data or null if no packet available.</returns>
    public byte[]? ReceivePacket()
    {
        if (_session == IntPtr.Zero)
            return null;

        var packetPtr = WinTun.WintunReceivePacket(_session, out var packetSize);

        if (packetPtr == IntPtr.Zero)
            return null;

        try
        {
            var packet = new byte[packetSize];
            Marshal.Copy(packetPtr, packet, 0, (int)packetSize);
            return packet;
        }
        finally
        {
            WinTun.WintunReleaseReceivePacket(_session, packetPtr);
        }
    }

    /// <summary>
    /// Sends a packet through the adapter.
    /// </summary>
    /// <param name="packet">Packet data to send.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Unit> SendPacket(byte[] packet)
    {
        if (_session == IntPtr.Zero)
        {
            return Result<Unit>.Failure("No active session");
        }

        if (packet.Length > WinTun.MaxIpPacketSize)
        {
            return Result<Unit>.Failure($"Packet too large: {packet.Length} > {WinTun.MaxIpPacketSize}");
        }

        var packetPtr = WinTun.WintunAllocateSendPacket(_session, (uint)packet.Length);

        if (packetPtr == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return Result<Unit>.Failure($"Failed to allocate send packet: {new Win32Exception(error).Message}");
        }

        Marshal.Copy(packet, 0, packetPtr, packet.Length);
        WinTun.WintunSendPacket(_session, packetPtr);

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>
    /// Sets the IP address on this adapter using netsh.
    /// </summary>
    /// <param name="ipAddress">IP address to assign.</param>
    /// <param name="subnetMask">Subnet mask.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Unit> SetIpAddress(IPAddress ipAddress, IPAddress subnetMask)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"interface ip set address name=\"{_name}\" static {ipAddress} {subnetMask}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(5000);

            if (process?.ExitCode != 0)
            {
                var error = process?.StandardError.ReadToEnd();
                return Result<Unit>.Failure($"Failed to set IP address: {error}");
            }

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure($"Exception setting IP address: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        EndSession();

        if (_adapter != IntPtr.Zero)
        {
            WinTun.WintunCloseAdapter(_adapter);
            _adapter = IntPtr.Zero;
        }

        _disposed = true;
    }
}

/// <summary>
/// Simple Result type for error handling.
/// </summary>
/// <typeparam name="T">Success value type.</typeparam>
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}

/// <summary>
/// Unit type for void results.
/// </summary>
public readonly struct Unit
{
    public static readonly Unit Value = new();
}
