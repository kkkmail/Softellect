using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

// ReSharper disable once CheckNamespace
namespace Softellect.Vpn.Interop;

public static class PhysicalNetworkDetector
{
    private const string VpnInterfaceAliasToExclude = "SoftellectVPN";

    private static void WriteLine(string s)
    {
#if DEBUG
        Console.WriteLine(s);
#endif
    }

    public static string GetPhysicalGatewayIpv4()
    {
        var (gw, _) = GetPhysicalGatewayAndInterfaceAlias();
        return gw;
    }

    public static string GetPhysicalInterfaceName()
    {
        var (_, iface) = GetPhysicalGatewayAndInterfaceAlias();
        return iface;
    }

    public static (string gatewayIp, string interfaceAlias) GetPhysicalGatewayAndInterface() =>
        GetPhysicalGatewayAndInterfaceAlias();

    #region Core logic

    private static unsafe (string gatewayIp, string interfaceAlias) GetPhysicalGatewayAndInterfaceAlias()
    {
        var tablePtr = IntPtr.Zero;

        try
        {
            var err = GetIpForwardTable2(ADDRESS_FAMILY.AF_INET, out tablePtr);

            if (err != 0)
            {
                throw new Win32Exception(err, "GetIpForwardTable2(AF_INET) failed.");
            }

            var numEntries = (uint)Marshal.ReadInt32(tablePtr);
            WriteLine($"[IPHLAPI] Route table entries: {numEntries}");

            var rowSize = (uint)sizeof(MIB_IPFORWARD_ROW2);
            WriteLine($"[IPHLAPI] sizeof(MIB_IPFORWARD_ROW2) = {rowSize} bytes");

            // IMPORTANT:
            // MIB_IPFORWARD_TABLE2 has ULONG NumEntries, then Table[].
            // Table[] is 8-byte aligned because row begins with NET_LUID (ulong).
            var p = (byte*)tablePtr + sizeof(uint); // right after NumEntries

            var pRaw = (ulong)p;
            var pAligned = (pRaw + 7UL) & ~7UL; // align to 8

            var rowsBase = (byte*)pAligned;

            WriteLine(
                $"[IPHLAPI] tablePtr=0x{(ulong)tablePtr:X}, rowsBaseRaw=0x{pRaw:X}, rowsBaseAligned=0x{pAligned:X}, pad={(long)(pAligned - pRaw)}");

            string? bestGw = null;
            string? bestAlias = null;
            var bestMetric = uint.MaxValue;

            for (uint i = 0; i < numEntries; i++)
            {
                MIB_IPFORWARD_ROW2 row;
                try
                {
                    row = *(MIB_IPFORWARD_ROW2*)(rowsBase + (i * rowSize));
                }
                catch (Exception ex)
                {
                    WriteLine($"[IPHLAPI] row#{i:D2}: FAILED TO READ ROW: {ex}");
                    continue;
                }

                string alias;
                try
                {
                    alias = ConvertLuidToAlias(row.InterfaceLuid);
                }
                catch (Exception ex)
                {
                    alias = $"<alias-error: {ex.GetType().Name}: {ex.Message}>";
                }

                var destFamily = row.DestinationPrefix.Prefix.si_family;
                var destPrefixLen = row.DestinationPrefix.PrefixLength;
                var destAddr = FormatSockaddrInet(row.DestinationPrefix.Prefix);

                var hopFamily = row.NextHop.si_family;
                var nextHopAddr = FormatSockaddrInet(row.NextHop);

                WriteLine(
                    $"[IPHLAPI] row#{i:D2} " +
                    $"ifIndex={row.InterfaceIndex} alias='{alias}' " +
                    $"destFam={destFamily} dest='{destAddr}/{destPrefixLen}' " +
                    $"hopFam={hopFamily} nextHop='{nextHopAddr}' " +
                    $"metric={row.Metric} proto={row.Protocol} " +
                    $"loop={row.Loopback} pub={row.Publish} immortal={row.Immortal} " +
                    $"age={row.Age} origin={row.Origin}");

                var isIpv4Default =
                    destFamily == (ushort)ADDRESS_FAMILY.AF_INET &&
                    destPrefixLen == 0 &&
                    row.DestinationPrefix.Prefix.Ipv4.sin_addr.S_addr == 0;

                if (!isIpv4Default)
                {
                    continue;
                }

                if (string.Equals(alias, VpnInterfaceAliasToExclude, StringComparison.OrdinalIgnoreCase))
                {
                    WriteLine($"[IPHLAPI] row#{i:D2} -> default route but EXCLUDED (VPN alias match)");
                    continue;
                }

                if (hopFamily != (ushort)ADDRESS_FAMILY.AF_INET)
                {
                    WriteLine($"[IPHLAPI] row#{i:D2} -> default route but SKIPPED (nextHop not IPv4)");
                    continue;
                }

                var gw = ExtractIpv4FromSockaddrInet(row.NextHop);

                if (string.IsNullOrWhiteSpace(gw) || gw == "0.0.0.0")
                {
                    WriteLine($"[IPHLAPI] row#{i:D2} -> default route but SKIPPED (gateway='{gw}')");
                    continue;
                }

                if (row.Metric < bestMetric)
                {
                    bestMetric = row.Metric;
                    bestGw = gw;
                    bestAlias = alias;
                    WriteLine(
                        $"[IPHLAPI] row#{i:D2} -> BEST SO FAR: gw='{bestGw}', iface='{bestAlias}', metric={bestMetric}");
                }
            }

            if (bestGw == null || bestAlias == null)
            {
                throw new InvalidOperationException(
                    $"No physical IPv4 default route found after excluding '{VpnInterfaceAliasToExclude}'");
            }

            WriteLine($"[IPHLAPI] FINAL: gw='{bestGw}', iface='{bestAlias}', metric={bestMetric}");
            return (bestGw, bestAlias);
        }
        finally
        {
            if (tablePtr != IntPtr.Zero)
            {
                FreeMibTable(tablePtr);
            }
        }
    }

    private static string ConvertLuidToAlias(NET_LUID luid)
    {
        const int IF_MAX_STRING_SIZE = 256;
        var sb = new StringBuilder(IF_MAX_STRING_SIZE);

        var err = ConvertInterfaceLuidToAlias(ref luid, sb, sb.Capacity);

        if (err != 0)
        {
            throw new Win32Exception(err, "ConvertInterfaceLuidToAlias failed.");
        }

        return sb.ToString();
    }

    private static unsafe string FormatSockaddrInet(SOCKADDR_INET sa)
    {
        if (sa.si_family == (ushort)ADDRESS_FAMILY.AF_INET)
        {
            var s_addr = sa.Ipv4.sin_addr.S_addr;
            var bytes = BitConverter.GetBytes(s_addr);
            return new IPAddress(bytes).ToString();
        }

        if (sa.si_family == (ushort)ADDRESS_FAMILY.AF_INET6)
        {
            Span<byte> b = stackalloc byte[16];
            for (var i = 0; i < 16; i++)
                b[i] = sa.Ipv6.sin6_addr.Byte[i];

            try
            {
                return new IPAddress(b.ToArray()).ToString();
            }
            catch
            {
                return "<ipv6>";
            }
        }

        return "<unspec>";
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

    #region Native structs (blittable)

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
    private unsafe struct SOCKADDR_IN
    {
        public short sin_family;
        public ushort sin_port;
        public IN_ADDR sin_addr;
        public fixed byte sin_zero[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IN6_ADDR
    {
        public fixed byte Byte[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SOCKADDR_IN6
    {
        public short sin6_family;
        public ushort sin6_port;
        public uint sin6_flowinfo;
        public IN6_ADDR sin6_addr;
        public uint sin6_scope_id;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct SOCKADDR_INET
    {
        [FieldOffset(0)] public ushort si_family;

        [FieldOffset(0)] public SOCKADDR_IN Ipv4;

        [FieldOffset(0)] public SOCKADDR_IN6 Ipv6;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IP_ADDRESS_PREFIX
    {
        public SOCKADDR_INET Prefix;
        public byte PrefixLength;
        public fixed byte Padding[3]; // critical
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MIB_IPFORWARD_ROW2
    {
        public NET_LUID InterfaceLuid;
        public uint InterfaceIndex;

        public IP_ADDRESS_PREFIX DestinationPrefix;
        public SOCKADDR_INET NextHop;

        public byte SitePrefixLength;
        public fixed byte Padding1[3];

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

    #endregion

    #region PInvoke

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpForwardTable2(
        ADDRESS_FAMILY Family,
        out IntPtr Table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr Memory);

    [DllImport("iphlpapi.dll", CharSet = CharSet.Unicode)]
    private static extern int ConvertInterfaceLuidToAlias(
        ref NET_LUID InterfaceLuid,
        StringBuilder InterfaceAlias,
        int Length);

    #endregion
}
