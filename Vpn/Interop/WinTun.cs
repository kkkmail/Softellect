using System;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace Softellect.Vpn.Interop;

/// <summary>
/// P/Invoke bindings for WinTun driver.
/// See: https://git.zx2c4.com/wintun/about/
/// </summary>
public static class WinTun
{
    private const string DllName = "wintun.dll";

    public static readonly Guid WinTunGuid = new("88888888-8888-8888-8888-888888888888");

    /// <summary>
    /// Minimum ring capacity.
    /// </summary>
    public const uint MinRingCapacity = 0x20000; // 128 KB

    /// <summary>
    /// Maximum ring capacity.
    /// </summary>
    public const uint MaxRingCapacity = 0x4000000; // 64 MB

    /// <summary>
    /// Maximum IP packet size.
    /// </summary>
    public const int MaxIpPacketSize = 0xFFFF;

    /// <summary>
    /// Creates a new WinTun adapter.
    /// </summary>
    /// <param name="name">Adapter name (max 127 chars).</param>
    /// <param name="tunnelType">Tunnel type name.</param>
    /// <param name="requestedGuid">Optional GUID for the adapter.</param>
    /// <returns>Adapter handle or IntPtr.Zero on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern IntPtr WintunCreateAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
        ref Guid requestedGuid);

    /// <summary>
    /// Opens an existing WinTun adapter.
    /// </summary>
    /// <param name="name">Adapter name.</param>
    /// <returns>Adapter handle or IntPtr.Zero on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern IntPtr WintunOpenAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name);

    /// <summary>
    /// Closes a WinTun adapter handle.
    /// </summary>
    /// <param name="adapter">Adapter handle.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunCloseAdapter(IntPtr adapter);

    /// <summary>
    /// Deletes a WinTun adapter driver and associated resources.
    /// </summary>
    /// <param name="adapter">Adapter handle.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunDeleteDriver(IntPtr adapter);

    /// <summary>
    /// Gets the LUID of the adapter.
    /// </summary>
    /// <param name="adapter">Adapter handle.</param>
    /// <param name="luid">Pointer to receive LUID.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunGetAdapterLUID(IntPtr adapter, out ulong luid);

    /// <summary>
    /// Starts a WinTun session.
    /// </summary>
    /// <param name="adapter">Adapter handle.</param>
    /// <param name="capacity">Ring capacity (must be between MinRingCapacity and MaxRingCapacity).</param>
    /// <returns>Session handle or IntPtr.Zero on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    /// <summary>
    /// Ends a WinTun session.
    /// </summary>
    /// <param name="session">Session handle.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunEndSession(IntPtr session);

    /// <summary>
    /// Gets a handle for waiting on packets.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <returns>Event handle.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

    /// <summary>
    /// Receives a packet from the adapter.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <param name="packetSize">Receives the packet size.</param>
    /// <returns>Pointer to packet data or IntPtr.Zero if no packet available.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    /// <summary>
    /// Releases a received packet back to the driver.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <param name="packet">Packet pointer from WintunReceivePacket.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    /// <summary>
    /// Allocates memory for sending a packet.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <param name="packetSize">Size of packet to send.</param>
    /// <returns>Pointer to buffer or IntPtr.Zero on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    /// <summary>
    /// Sends an allocated packet.
    /// </summary>
    /// <param name="session">Session handle.</param>
    /// <param name="packet">Packet pointer from WintunAllocateSendPacket.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunSendPacket(IntPtr session, IntPtr packet);

    /// <summary>
    /// Sets a logger callback for WinTun messages.
    /// </summary>
    /// <param name="callback">Logger callback or null to disable.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void WintunSetLogger(WintunLoggerCallback? callback);

    /// <summary>
    /// Logger callback delegate.
    /// </summary>
    /// <param name="level">Log level.</param>
    /// <param name="timestamp">Timestamp.</param>
    /// <param name="message">Log message.</param>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WintunLoggerCallback(
        WintunLoggerLevel level,
        ulong timestamp,
        [MarshalAs(UnmanagedType.LPWStr)] string message);

    /// <summary>
    /// WinTun logger levels.
    /// </summary>
    public enum WintunLoggerLevel
    {
        Info = 0,
        Warn = 1,
        Err = 2
    }
}
