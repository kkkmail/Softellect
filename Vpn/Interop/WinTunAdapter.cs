using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.FSharp.Core;
using Softellect.Sys;
using static Softellect.Vpn.Core.Primitives;

// ReSharper disable once CheckNamespace
namespace Softellect.Vpn.Interop;

/// <summary>
/// Managed wrapper for WinTun adapter operations.
/// </summary>
public sealed class WinTunAdapter : ITunAdapter
{
    private IntPtr _adapter;
    private IntPtr _session;
    private readonly string _name;
    private bool _disposed;
    private ulong _adapterLuid;
    private System.Threading.WaitHandle? _readWaitHandle;

    public string Name => _name;
    public bool IsSessionActive => _session != IntPtr.Zero;
    public ulong AdapterLuid => _adapterLuid;

    private WinTunAdapter(IntPtr adapter, string name)
    {
        _adapter = adapter;
        _name = name;
        WinTun.WintunGetAdapterLUID(adapter, out _adapterLuid);
    }

    #region Static Members

    /// <summary>
    /// Creates a new WinTun adapter.
    /// </summary>
    /// <param name="name">Adapter name.</param>
    /// <param name="tunnelType">Tunnel type identifier.</param>
    /// <param name="guid">Optional GUID for the adapter.</param>
    /// <returns>Result containing the adapter or error message.</returns>
    public static FSharpResult<ITunAdapter, string> Create(string name, string tunnelType, Guid? guid = null)
    {
        var adapterGuid = guid ?? Guid.NewGuid();
        var adapter = WinTun.WintunCreateAdapter(name, tunnelType, ref adapterGuid);

        if (adapter == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return FSharpResult<ITunAdapter, string>.NewError($"Failed to create WinTun adapter: {new Win32Exception(error).Message} (Error: {error})");
        }

        return FSharpResult<ITunAdapter, string>.NewOk(new WinTunAdapter(adapter, name));
    }

    /// <summary>
    /// Opens an existing WinTun adapter.
    /// </summary>
    /// <param name="name">Adapter name.</param>
    /// <returns>Result containing the adapter or error message.</returns>
    public static FSharpResult<ITunAdapter, string> Open(string name)
    {
        var adapter = WinTun.WintunOpenAdapter(name);

        if (adapter == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return FSharpResult<ITunAdapter, string>.NewError($"Failed to open WinTun adapter: {new Win32Exception(error).Message} (Error: {error})");
        }

        return FSharpResult<ITunAdapter, string>.NewOk(new WinTunAdapter(adapter, name));
    }

    /// <summary>
    /// Runs a command and returns the result.
    /// </summary>
    /// <param name="fileName">Executable name.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="operationName">Name of the operation for error messages.</param>
    /// <returns>Result indicating success or failure with stderr text.</returns>
    public static FSharpResult<Unit, string> RunCommand(string fileName, string arguments, string operationName)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(30_000);

            if (process?.ExitCode != 0)
            {
                var error = process?.StandardError.ReadToEnd();
                return FSharpResult<Unit, string>.NewError($"Failed to {operationName}: {error}");
            }

            return FSharpResult<Unit, string>.NewOk(Primitives.FSharpUnit);
        }
        catch (Exception ex)
        {
            return FSharpResult<Unit, string>.NewError($"Exception in {operationName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a netsh command and returns the result.
    /// </summary>
    /// <param name="arguments">Command arguments (without 'netsh' prefix).</param>
    /// <param name="operationName">Name of the operation for error messages.</param>
    /// <returns>Result indicating success or failure with stderr text.</returns>
    public static FSharpResult<Unit, string> RunNetsh(string arguments, string operationName)
    {
        return RunCommand("netsh", arguments, operationName);
    }

    /// <summary>
    /// Converts a subnet mask to CIDR notation.
    /// </summary>
    private static int MaskToCidr(string mask)
    {
        var parts = mask.Split('.');
        if (parts.Length != 4) return 0;

        int cidr = 0;
        foreach (var part in parts)
        {
            if (byte.TryParse(part, out var b))
            {
                while (b != 0)
                {
                    cidr += (int)(b & 1);
                    b >>= 1;
                }
            }
        }
        return cidr;
    }

    /// <summary>
    /// Flushes the DNS resolver cache using ipconfig.
    /// </summary>
    /// <returns>Result indicating success or failure.</returns>
    public static FSharpResult<Unit, string> FlushDns()
    {
        return RunCommand("ipconfig", "/flushdns", "flush DNS");
    }

    #endregion

    #region Interface Implementation

    /// <summary>
    /// Starts a packet session on this adapter.
    /// </summary>
    /// <param name="ringCapacity">Ring buffer capacity (default: 4MB).</param>
    /// <returns>Result indicating success or failure.</returns>
    public FSharpResult<Unit, string> StartSession(uint ringCapacity)
    {
        if (_session != IntPtr.Zero)
        {
            return FSharpResult<Unit, string>.NewError("Session already active");
        }

        if (ringCapacity < WinTun.MinRingCapacity || ringCapacity > WinTun.MaxRingCapacity)
        {
            return FSharpResult<Unit, string>.NewError($"Ring capacity must be between {WinTun.MinRingCapacity} and {WinTun.MaxRingCapacity}");
        }

        _session = WinTun.WintunStartSession(_adapter, ringCapacity);

        if (_session == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return FSharpResult<Unit, string>.NewError($"Failed to start session: {new Win32Exception(error).Message} (Error: {error})");
        }

        _readWaitHandle = null;
        return FSharpResult<Unit, string>.NewOk(Primitives.FSharpUnit);
    }

    public FSharpResult<Unit, string> StartSession() => StartSession(0x400000);

    /// <summary>
    /// Ends the current packet session.
    /// </summary>
    public void EndSession()
    {
        if (_readWaitHandle != null)
        {
            _readWaitHandle.Dispose();
            _readWaitHandle = null;
        }

        if (_session != IntPtr.Zero)
        {
            WinTun.WintunEndSession(_session);
            _session = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets the wait event handle for incoming packets (native).
    /// </summary>
    /// <returns>Event handle or IntPtr.Zero if no session.</returns>
    public IntPtr GetReadWaitEvent()
    {
        return _session != IntPtr.Zero ? WinTun.WintunGetReadWaitEvent(_session) : IntPtr.Zero;
    }

    /// <summary>
    /// Gets a managed WaitHandle for the read event, suitable for WaitHandle.WaitAny.
    /// </summary>
    /// <returns>WaitHandle or null if no session.</returns>
    public System.Threading.WaitHandle? GetReadWaitHandle()
    {
        if (_session == IntPtr.Zero)
            return null;

        if (_readWaitHandle == null)
        {
            var nativeHandle = WinTun.WintunGetReadWaitEvent(_session);
            if (nativeHandle == IntPtr.Zero)
                return null;

            var ewh = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset);
            ewh.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(nativeHandle, ownsHandle: false);
            _readWaitHandle = ewh;
        }

        return _readWaitHandle;
    }

    /// <summary>
    /// Receives a packet from the adapter.
    /// </summary>
    /// <returns>Packet data or null if no packet available.</returns>
    public FSharpOption<byte[]> ReceivePacket()
    {
        if (_session == IntPtr.Zero)
        {
            return FSharpOption<byte[]>.None;
        }

        var packetPtr = WinTun.WintunReceivePacket(_session, out var packetSize);

        if (packetPtr == IntPtr.Zero)
        {
            return FSharpOption<byte[]>.None;
        }

        try
        {
            var packet = new byte[packetSize];
            Marshal.Copy(packetPtr, packet, 0, (int)packetSize);
            return FSharpOption<byte[]>.Some(packet);
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
    public FSharpResult<Unit, string> SendPacket(byte[] packet)
    {
        if (_session == IntPtr.Zero)
        {
            return FSharpResult<Unit, string>.NewError("No active session");
        }

        if (packet.Length > WinTun.MaxIpPacketSize)
        {
            return FSharpResult<Unit, string>.NewError($"Packet too large: {packet.Length} > {WinTun.MaxIpPacketSize}");
        }

        var packetPtr = WinTun.WintunAllocateSendPacket(_session, (uint)packet.Length);

        if (packetPtr == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return FSharpResult<Unit, string>.NewError($"Failed to allocate send packet: {new Win32Exception(error).Message}");
        }

        Marshal.Copy(packet, 0, packetPtr, packet.Length);
        WinTun.WintunSendPacket(_session, packetPtr);

        return FSharpResult<Unit, string>.NewOk(Primitives.FSharpUnit);
    }

    /// <summary>
    /// Sets the IP address on this adapter using netsh.
    /// </summary>
    /// <param name="ipAddress">IP address to assign.</param>
    /// <param name="subnetMask">Subnet mask.</param>
    /// <returns>Result indicating success or failure.</returns>
    public FSharpResult<Unit, string> SetIpAddress(Primitives.IpAddress ipAddress, Primitives.IpAddress subnetMask)
    {
        return RunNetsh(
            $"interface ip set address name=\"{_name}\" static {ipAddress.value} {subnetMask.value}",
            "set IP address");
    }

    /// <summary>
    /// Sets the DNS server on this adapter using netsh.
    /// </summary>
    /// <param name="dnsServerIp">DNS server IP address.</param>
    /// <returns>Result indicating success or failure.</returns>
    public FSharpResult<Unit, string> SetDnsServer(Primitives.IpAddress dnsServerIp)
    {
        return RunNetsh(
            $"interface ip set dns name=\"{_name}\" static {dnsServerIp.value}",
            "set DNS server");
    }

    /// <summary>
    /// Adds a route via this adapter using netsh.
    /// </summary>
    /// <param name="destination">Destination network address.</param>
    /// <param name="mask">Subnet mask for the route.</param>
    /// <param name="gateway">Gateway IP address.</param>
    /// <param name="metric">Route metric.</param>
    /// <returns>Result indicating success or failure.</returns>
    public FSharpResult<Unit, string> AddRoute(Primitives.IpAddress destination, Primitives.IpAddress mask, Primitives.IpAddress gateway, int metric)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"interface ipv4 add route {destination.value}/{MaskToCidr(mask.value)} \"{_name}\" {gateway.value} metric={metric}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(5000);

            if (process?.ExitCode != 0)
            {
                var stderr = process?.StandardError.ReadToEnd() ?? "";
                // Treat "already exists" as success for idempotency
                if (stderr.Contains("exists", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("object already exists", StringComparison.OrdinalIgnoreCase))
                {
                    return FSharpResult<Unit, string>.NewOk(Primitives.FSharpUnit);
                }
                return FSharpResult<Unit, string>.NewError($"Failed to add route: {stderr}");
            }

            return FSharpResult<Unit, string>.NewOk(Primitives.FSharpUnit);
        }
        catch (Exception ex)
        {
            return FSharpResult<Unit, string>.NewError($"Exception adding route: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the interface metric on this adapter using netsh.
    /// </summary>
    /// <param name="metric">Interface metric value.</param>
    /// <returns>Result indicating success or failure.</returns>
    public FSharpResult<Unit, string> SetInterfaceMetric(int metric)
    {
        return RunNetsh(
            $"interface ipv4 set interface \"{_name}\" metric={metric}",
            "set interface metric");
    }

    /// <summary>
    /// Sets the MTU on this adapter using netsh.
    /// </summary>
    /// <param name="mtu">MTU value in bytes.</param>
    /// <returns>Result indicating success or failure.</returns>
    public FSharpResult<Unit, string> SetMtu(int mtu)
    {
        if (mtu < 576 || mtu > 9000)
            return FSharpResult<Unit, string>.NewError($"Invalid MTU: {mtu}");

        // "subinterface" is the correct netsh target for MTU.
        // Name must match the interface name as shown by:
        //   netsh interface ipv4 show subinterfaces
        return RunNetsh(
            $"interface ipv4 set subinterface \"{_name}\" mtu={mtu} store=persistent",
            "set MTU");
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

    #endregion
}
