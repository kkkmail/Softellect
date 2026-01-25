using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

// ReSharper disable once CheckNamespace
namespace Softellect.Vpn.Interop;

/// <summary>
/// Windows IP Helper API based detection of the currently-preferred IPv4 gateway and its interface name.
/// Intended to replace fragile parsing of `netsh` output.
/// </summary>
public static class PhysicalNetworkDetector
{
    /// <summary>
    /// Returns the IPv4 gateway address (next hop) that Windows would use for an external IPv4 destination.
    /// </summary>
    public static string GetPhysicalGatewayIpv4()
    {
        var info = GetGatewayAndInterfaceAlias();
        return info.gatewayIp;
    }

    /// <summary>
    /// Returns the interface alias (friendly name) for the interface Windows would use for an external IPv4 destination.
    /// </summary>
    public static string GetPhysicalInterfaceName()
    {
        var info = GetGatewayAndInterfaceAlias();
        return info.interfaceAlias;
    }

    #region Implementation

    private static (string gatewayIp, string interfaceAlias) GetGatewayAndInterfaceAlias()
    {
        // Pick a well-known public IPv4 address to force Windows to select the real “best” route.
        // This mirrors how routing is actually chosen, rather than scraping “0.0.0.0/0”.
        var destination = CreateSockaddrInetIPv4(IPAddress.Parse("8.8.8.8"));

        var err = GetBestRoute2(
            IntPtr.Zero, // InterfaceLuid (optional)
            0, // InterfaceIndex (optional)
            IntPtr.Zero, // SourceAddress (optional)
            ref destination, // DestinationAddress
            0, // AddressSortOptions
            out MIB_IPFORWARD_ROW2 bestRoute,
            out SOCKADDR_INET bestSourceAddress);

        if (err != 0)
        {
            throw new Win32Exception(err, $"GetBestRoute2 failed (err={err}).");
        }

        // NextHop is the gateway for the selected route. It may be 0.0.0.0 for on-link routes.
        var gateway = ExtractIpv4FromSockaddrInet(bestRoute.NextHop);

        // Convert InterfaceLuid -> alias (friendly name, e.g. "Wi-Fi", "Ethernet")
        var alias = ConvertLuidToAlias(bestRoute.InterfaceLuid);

        return (gateway, alias);
    }

    private static string ConvertLuidToAlias(NET_LUID luid)
    {
        // IF_MAX_STRING_SIZE is 256. The API expects a caller-provided buffer (wide chars).
        const int IF_MAX_STRING_SIZE = 256;
        var sb = new StringBuilder(IF_MAX_STRING_SIZE + 1);

        var err = ConvertInterfaceLuidToAlias(ref luid, sb, sb.Capacity);

        if (err != 0)
        {
            throw new Win32Exception(err, $"ConvertInterfaceLuidToAlias failed (err={err}).");
        }

        return sb.ToString();
    }

    private static SOCKADDR_INET CreateSockaddrInetIPv4(IPAddress ipv4)
    {
        if (ipv4.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException("IPv4 address required.", nameof(ipv4));
        }

        byte[] bytes = ipv4.GetAddressBytes();

        // SOCKADDR_INET is a union; we fill the IPv4 view.
        var sa = new SOCKADDR_INET
        {
            si_family = (ushort)ADDRESS_FAMILY.AF_INET,
            Ipv4 = new SOCKADDR_IN
            {
                sin_family = (short)ADDRESS_FAMILY.AF_INET,
                // sin_port is irrelevant for routing lookup; keep 0.
                sin_port = 0,
                // sin_addr in network byte order for the 32-bit integer view.
                sin_addr = new IN_ADDR { S_addr = BitConverter.ToUInt32(bytes, 0) },
                sin_zero = new byte[8]
            }
        };

        return sa;
    }

    private static string ExtractIpv4FromSockaddrInet(SOCKADDR_INET sa)
    {
        if (sa.si_family != (ushort)ADDRESS_FAMILY.AF_INET)
        {
            return string.Empty;
        }

        // IN_ADDR is stored as uint in network byte order already.
        var s_addr = sa.Ipv4.sin_addr.S_addr;
        var bytes = BitConverter.GetBytes(s_addr);
        return new IPAddress(bytes).ToString();
    }

    #endregion

    #region P/Invoke

    private enum ADDRESS_FAMILY : ushort
    {
        AF_UNSPEC = 0,
        AF_INET = 2,
        AF_INET6 = 23
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NET_LUID
    {
        public ulong Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IN_ADDR
    {
        public uint S_addr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SOCKADDR_IN
    {
        public short sin_family; // AF_INET
        public ushort sin_port; // network byte order
        public IN_ADDR sin_addr; // IPv4 address

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] sin_zero;
    }

    // Minimal IPv6 definition to satisfy union layout (we don’t use it here)
    [StructLayout(LayoutKind.Sequential)]
    private struct IN6_ADDR
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Byte;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SOCKADDR_IN6
    {
        public short sin6_family; // AF_INET6
        public ushort sin6_port; // network byte order
        public uint sin6_flowinfo;
        public IN6_ADDR sin6_addr;
        public uint sin6_scope_id;
    }

    // SOCKADDR_INET is a union of IPv4/IPv6. This layout matches the native union size.
    [StructLayout(LayoutKind.Explicit)]
    private struct SOCKADDR_INET
    {
        [FieldOffset(0)] public ushort si_family;

        [FieldOffset(0)] public SOCKADDR_IN Ipv4;

        [FieldOffset(0)] public SOCKADDR_IN6 Ipv6;
    }

    // MIB_IPFORWARD_ROW2 is large; we include fields we need + padding for correctness.
    // The layout below matches the native struct ordering up to the fields we access.
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPFORWARD_ROW2
    {
        public NET_LUID InterfaceLuid;
        public uint InterfaceIndex;
        public SOCKADDR_INET DestinationPrefixPrefix; // placeholder start of IP_ADDRESS_PREFIX
        public byte DestinationPrefixPrefixLength; // placeholder

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] _destPrefixPad;

        public SOCKADDR_INET NextHop;

        public byte SitePrefixLength;
        public byte ValidLifetime; // placeholder
        public byte PreferredLifetime; // placeholder
        public byte Metric; // placeholder

        public uint Protocol; // placeholder
        public byte Loopback; // placeholder
        public byte AutoconfigureAddress; // placeholder
        public byte Publish; // placeholder
        public byte Immortal; // placeholder

        public uint Age; // placeholder
        public uint Origin; // placeholder
    }

    // NOTE:
    // The above MIB_IPFORWARD_ROW2 definition is intentionally “minimal-ish” to avoid pulling the entire
    // native struct. We only rely on InterfaceLuid and NextHop being at the correct offsets.
    // If you already have full iphlpapi interop infrastructure in this project, feel free to swap in
    // your canonical definitions.

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern int GetBestRoute2(
        IntPtr interfaceLuid,
        uint interfaceIndex,
        IntPtr sourceAddress,
        ref SOCKADDR_INET destinationAddress,
        uint addressSortOptions,
        out MIB_IPFORWARD_ROW2 bestRoute,
        out SOCKADDR_INET bestSourceAddress);

    [DllImport("iphlpapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int ConvertInterfaceLuidToAlias(
        ref NET_LUID interfaceLuid,
        [Out] StringBuilder interfaceAlias,
        int length);

    #endregion
}
