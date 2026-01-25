using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

// ReSharper disable once CheckNamespace
namespace Softellect.Vpn.Interop;

public static class PhysicalNetworkDetector
{
    public static string GetPhysicalGatewayIpv4()
    {
        var info = GetGatewayAndInterfaceAlias();
        return info.gatewayIp;
    }

    public static string GetPhysicalInterfaceName()
    {
        var info = GetGatewayAndInterfaceAlias();
        return info.interfaceAlias;
    }

    #region Implementation

    private static (string gatewayIp, string interfaceAlias) GetGatewayAndInterfaceAlias()
    {
        // A stable public IPv4 destination to force selection of the real “best” route.
        var destination = CreateSockaddrInetIPv4(IPAddress.Parse("8.8.8.8"));

        var err = GetBestRoute2(
            IntPtr.Zero, // InterfaceLuid (optional)
            0,           // InterfaceIndex (optional)
            IntPtr.Zero, // SourceAddress (optional)
            ref destination,
            0,
            out MIB_IPFORWARD_ROW2 bestRoute,
            out SOCKADDR_INET _);

        if (err != 0)
        {
            throw new Win32Exception(err, $"GetBestRoute2 failed (err={err}).");
        }

        var gateway = ExtractIpv4FromSockaddrInet(bestRoute.NextHop);
        var alias = ConvertLuidToAlias(bestRoute.InterfaceLuid);

        return (gateway, alias);
    }

    private static string ConvertLuidToAlias(NET_LUID luid)
    {
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

        var bytes = ipv4.GetAddressBytes();

        // SOCKADDR_INET is a union; fill IPv4 view.
        SOCKADDR_INET sa = default;
        sa.si_family = (ushort)ADDRESS_FAMILY.AF_INET;
        sa.Ipv4.sin_family = (short)ADDRESS_FAMILY.AF_INET;
        sa.Ipv4.sin_port = 0;

        // Note: BitConverter.ToUInt32 uses machine endianness, but IPAddress(byte[]) expects same ordering
        // when we reverse it back with GetBytes below. This is consistent as long as we treat it symmetrically.
        sa.Ipv4.sin_addr.S_addr = BitConverter.ToUInt32(bytes, 0);

        return sa;
    }

    private static string ExtractIpv4FromSockaddrInet(SOCKADDR_INET sa)
    {
        if (sa.si_family != (ushort)ADDRESS_FAMILY.AF_INET)
        {
            return string.Empty;
        }

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

    // IMPORTANT: blittable (no byte[])
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SOCKADDR_IN
    {
        public short sin_family;  // AF_INET
        public ushort sin_port;   // network byte order
        public IN_ADDR sin_addr;  // IPv4 address
        public fixed byte sin_zero[8];
    }

    // IMPORTANT: blittable (no byte[])
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IN6_ADDR
    {
        public fixed byte Byte[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SOCKADDR_IN6
    {
        public short sin6_family; // AF_INET6
        public ushort sin6_port;  // network byte order
        public uint sin6_flowinfo;
        public IN6_ADDR sin6_addr;
        public uint sin6_scope_id;
    }

    // Union of IPv4/IPv6. Now safe because the overlapped structs are blittable.
    [StructLayout(LayoutKind.Explicit)]
    private struct SOCKADDR_INET
    {
        [FieldOffset(0)] public ushort si_family;

        [FieldOffset(0)] public SOCKADDR_IN Ipv4;

        [FieldOffset(0)] public SOCKADDR_IN6 Ipv6;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IP_ADDRESS_PREFIX
    {
        public SOCKADDR_INET Prefix;
        public byte PrefixLength;

        // Native struct has implicit padding; make it explicit for stable layout.
        // (This matches typical SDK packing; keeps following fields aligned.)
        public byte Pad1;
        public ushort Pad2;
    }

    // This matches the SDK layout sufficiently for InterfaceLuid + NextHop.
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPFORWARD_ROW2
    {
        public NET_LUID InterfaceLuid;
        public uint InterfaceIndex;

        public IP_ADDRESS_PREFIX DestinationPrefix;
        public SOCKADDR_INET NextHop;

        public byte SitePrefixLength;
        public byte Pad1;
        public ushort Pad2;

        public uint ValidLifetime;
        public uint PreferredLifetime;
        public uint Metric;
        public uint Protocol;

        public byte Loopback;
        public byte AutoconfigureAddress;
        public byte Publish;
        public byte Immortal;

        public uint Age;
        public uint Origin;
    }

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
