using System;
using System.Net;
using System.Runtime.InteropServices;

namespace Softellect.Vpn.Interop;

/// <summary>
/// P/Invoke bindings for Windows Filtering Platform (WFP).
/// Used to implement the kill-switch functionality.
/// </summary>
public static class WindowsFilteringPlatform
{
    private const string Fwpuclnt = "fwpuclnt.dll";

    // WFP Error codes
    public const uint ErrorSuccess = 0;
    public const uint FwpEAlreadyExists = 0x80320009;

    // Layer GUIDs
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");

    // Sublayer GUID for our filters
    public static readonly Guid VpnSublayerGuid = new("77777777-7777-7777-7777-777777777777");

    // Condition field GUIDs
    public static readonly Guid FWPM_CONDITION_IP_REMOTE_ADDRESS = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    public static readonly Guid FWPM_CONDITION_IP_LOCAL_ADDRESS = new("d9ee00de-c1ef-4617-bfe3-ffd8f5a08957");

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmEngineOpen0(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        uint authnService,
        IntPtr authIdentity,
        IntPtr session,
        out IntPtr engineHandle);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmSubLayerAdd0(
        IntPtr engineHandle,
        ref FWPM_SUBLAYER0 subLayer,
        IntPtr sd);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmSubLayerDeleteByKey0(
        IntPtr engineHandle,
        ref Guid key);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmFilterAdd0(
        IntPtr engineHandle,
        ref FWPM_FILTER0 filter,
        IntPtr sd,
        out ulong filterId);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmFilterDeleteById0(
        IntPtr engineHandle,
        ulong filterId);

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_DISPLAY_DATA0
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string name;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_BYTE_BLOB
    {
        public uint size;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public IntPtr filterCondition;
        public FWPM_ACTION0 action;
        public ulong rawContext;
        public IntPtr reserved;
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_VALUE0
    {
        public uint type;
        public FWP_VALUE0_UNION value;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct FWP_VALUE0_UNION
    {
        [FieldOffset(0)] public byte uint8;
        [FieldOffset(0)] public ushort uint16;
        [FieldOffset(0)] public uint uint32;
        [FieldOffset(0)] public ulong uint64;
        [FieldOffset(0)] public IntPtr byteBlob;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;
        public uint matchType;
        public FWP_CONDITION_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_CONDITION_VALUE0
    {
        public uint type;
        public FWP_CONDITION_VALUE0_UNION value;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct FWP_CONDITION_VALUE0_UNION
    {
        [FieldOffset(0)] public byte uint8;
        [FieldOffset(0)] public ushort uint16;
        [FieldOffset(0)] public uint uint32;
        [FieldOffset(0)] public ulong uint64;
        [FieldOffset(0)] public IntPtr byteArray16;
        [FieldOffset(0)] public IntPtr v4AddrMask;
        [FieldOffset(0)] public IntPtr v6AddrMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_V4_ADDR_AND_MASK
    {
        public uint addr;
        public uint mask;
    }

    // Action types
    public const uint FWP_ACTION_BLOCK = 0x0001;
    public const uint FWP_ACTION_PERMIT = 0x0002;

    // Match types
    public const uint FWP_MATCH_EQUAL = 0;
    public const uint FWP_MATCH_NOT_EQUAL = 7;

    // Value types
    public const uint FWP_UINT8 = 0;
    public const uint FWP_UINT16 = 1;
    public const uint FWP_UINT32 = 2;
    public const uint FWP_UINT64 = 3;
    public const uint FWP_V4_ADDR_MASK = 11;
    public const uint FWP_V6_ADDR_MASK = 12;
    public const uint FWP_EMPTY = 0;
}
