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

    // RPC authentication service constants
    public const uint RPC_C_AUTHN_WINNT = 10;

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

    [StructLayout(LayoutKind.Explicit, Size = 200)]
    public struct FWPM_FILTER0
    {
        [FieldOffset(0)] public Guid filterKey;                    // 16 bytes (0-15)
        [FieldOffset(16)] public IntPtr displayDataName;            // 8 bytes (16-23) - pointer to name string
        [FieldOffset(24)] public IntPtr displayDataDescription;     // 8 bytes (24-31) - pointer to description string
        [FieldOffset(32)] public uint flags;                        // 4 bytes (32-35) + 4 padding
        [FieldOffset(40)] public IntPtr providerKey;                // 8 bytes (40-47)
        [FieldOffset(48)] public uint providerDataSize;             // 4 bytes (48-51) + 4 padding
        [FieldOffset(56)] public IntPtr providerDataPtr;            // 8 bytes (56-63)
        [FieldOffset(64)] public Guid layerKey;                     // 16 bytes (64-79)
        [FieldOffset(80)] public Guid subLayerKey;                  // 16 bytes (80-95)
        [FieldOffset(96)] public uint weightType;                   // 4 bytes (96-99) + 4 padding
        [FieldOffset(104)] public ulong weightValue;                // 8 bytes (104-111) - union of all value types
        [FieldOffset(112)] public uint numFilterConditions;         // 4 bytes (112-115) + 4 padding
        [FieldOffset(120)] public IntPtr filterCondition;           // 8 bytes (120-127)
        [FieldOffset(128)] public uint actionType;                  // 4 bytes (128-131)
        [FieldOffset(132)] public Guid actionFilterType;            // 16 bytes (132-147) + 4 padding at 148
        [FieldOffset(152)] public ulong rawContext;                 // 8 bytes (152-159)
        [FieldOffset(160)] public IntPtr reserved;                  // 8 bytes (160-167)
        [FieldOffset(168)] public ulong filterId;                   // 8 bytes (168-175)
        [FieldOffset(176)] public uint effectiveWeightType;         // 4 bytes (176-179) + 4 padding
        [FieldOffset(184)] public ulong effectiveWeightValue;       // 8 bytes (184-191)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct FWP_VALUE0
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public byte uint8;
        [FieldOffset(8)] public ushort uint16;
        [FieldOffset(8)] public uint uint32;
        [FieldOffset(8)] public ulong uint64;
        [FieldOffset(8)] public IntPtr byteBlob;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct FWPM_FILTER_CONDITION0
    {
        [FieldOffset(0)] public Guid fieldKey;      // 16 bytes
        [FieldOffset(16)] public uint matchType;    // 4 bytes + 4 padding
        [FieldOffset(24)] public uint valueType;    // 4 bytes + 4 padding (FWP_CONDITION_VALUE0.type)
        [FieldOffset(32)] public IntPtr valuePtr;   // 8 bytes (FWP_CONDITION_VALUE0.value)
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
