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
    public const uint RPC_C_AUTHN_DEFAULT = 0xFFFFFFFF;

    // Session flags
    public const uint FWPM_SESSION_FLAG_DYNAMIC = 0x00000001;

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
    public static extern uint FwpmFilterAdd0(
        IntPtr engineHandle,
        IntPtr filter,
        IntPtr sd,
        out ulong filterId);

    [DllImport(Fwpuclnt, SetLastError = true)]
    public static extern uint FwpmFilterDeleteById0(
        IntPtr engineHandle,
        ulong filterId);

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_SESSION0
    {
        public Guid sessionKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public uint txnWaitTimeoutInMSec;
        public uint processId;
        public IntPtr sid;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string username;
        [MarshalAs(UnmanagedType.Bool)]
        public bool kernelMode;
    }

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

    // FWPM_FILTER0 - Explicit layout with exact offsets from Windows SDK
    // Total size on x64: 192 bytes
    [StructLayout(LayoutKind.Explicit)]
    public struct FWPM_FILTER0
    {
        [FieldOffset(0)] public Guid filterKey;              // 0-15: GUID (16 bytes)
        [FieldOffset(16)] public IntPtr displayDataName;     // 16-23: LPWSTR name
        [FieldOffset(24)] public IntPtr displayDataDesc;     // 24-31: LPWSTR description
        [FieldOffset(32)] public uint flags;                 // 32-35: UINT32 (4 bytes + 4 pad)
        [FieldOffset(40)] public IntPtr providerKey;         // 40-47: GUID* (8 bytes)
        [FieldOffset(48)] public uint providerDataSize;      // 48-51: UINT32
        [FieldOffset(56)] public IntPtr providerDataData;    // 56-63: UINT8* (aligned to 8)
        [FieldOffset(64)] public Guid layerKey;              // 64-79: GUID (16 bytes)
        [FieldOffset(80)] public Guid subLayerKey;           // 80-95: GUID (16 bytes)
        [FieldOffset(96)] public uint weightType;            // 96-99: FWP_DATA_TYPE
        [FieldOffset(104)] public ulong weightValue;         // 104-111: union (8 bytes, aligned)
        [FieldOffset(112)] public uint numFilterConditions;  // 112-115: UINT32 (4 bytes + 4 pad)
        [FieldOffset(120)] public IntPtr filterCondition;    // 120-127: FWPM_FILTER_CONDITION0*
        [FieldOffset(128)] public uint actionType;           // 128-131: FWP_ACTION_TYPE
        [FieldOffset(132)] public Guid actionFilterType;     // 132-147: GUID (16 bytes)
        [FieldOffset(152)] public ulong rawContext;          // 152-159: UINT64 (aligned to 8)
        [FieldOffset(160)] public IntPtr reserved;           // 160-167: void*
        [FieldOffset(168)] public ulong filterId;            // 168-175: UINT64
        [FieldOffset(176)] public uint effectiveWeightType;  // 176-179: FWP_DATA_TYPE
        [FieldOffset(184)] public ulong effectiveWeightValue;// 184-191: union (8 bytes)
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

    // FWPM_FILTER_CONDITION0 - Explicit layout
    // Total size on x64: 40 bytes
    [StructLayout(LayoutKind.Explicit)]
    public struct FWPM_FILTER_CONDITION0
    {
        [FieldOffset(0)] public Guid fieldKey;      // 0-15: GUID (16 bytes)
        [FieldOffset(16)] public uint matchType;    // 16-19: FWP_MATCH_TYPE (4 bytes + 4 pad)
        [FieldOffset(24)] public uint valueType;    // 24-27: FWP_DATA_TYPE (4 bytes + 4 pad)
        [FieldOffset(32)] public IntPtr valueData;  // 32-39: union value (8 bytes)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_V4_ADDR_AND_MASK
    {
        public uint addr;
        public uint mask;
    }

    // Action flags
    public const uint FWP_ACTION_FLAG_TERMINATING = 0x00001000;

    // Action types - must include FWP_ACTION_FLAG_TERMINATING for block/permit
    public const uint FWP_ACTION_BLOCK = 0x0001 | FWP_ACTION_FLAG_TERMINATING;   // = 0x1001
    public const uint FWP_ACTION_PERMIT = 0x0002 | FWP_ACTION_FLAG_TERMINATING;  // = 0x1002

    // Match types
    // Match types (FWP_MATCH_TYPE)
    public const uint FWP_MATCH_EQUAL = 0;
    public const uint FWP_MATCH_GREATER = 1;
    public const uint FWP_MATCH_LESS = 2;
    public const uint FWP_MATCH_GREATER_OR_EQUAL = 3;
    public const uint FWP_MATCH_LESS_OR_EQUAL = 4;
    public const uint FWP_MATCH_RANGE = 5;
    public const uint FWP_MATCH_FLAGS_ALL_SET = 6;
    public const uint FWP_MATCH_FLAGS_ANY_SET = 7;
    public const uint FWP_MATCH_FLAGS_NONE_SET = 8;
    public const uint FWP_MATCH_EQUAL_CASE_INSENSITIVE = 9;
    public const uint FWP_MATCH_NOT_EQUAL = 10;
    public const uint FWP_MATCH_TYPE_MAX = 11;

    // Value types (FWP_DATA_TYPE)
    public const uint FWP_EMPTY = 0;
    public const uint FWP_UINT8 = 1;
    public const uint FWP_UINT16 = 2;
    public const uint FWP_UINT32 = 3;
    public const uint FWP_UINT64 = 4;
    public const uint FWP_INT8 = 5;
    public const uint FWP_INT16 = 6;
    public const uint FWP_INT32 = 7;
    public const uint FWP_INT64 = 8;
    public const uint FWP_FLOAT = 9;
    public const uint FWP_DOUBLE = 10;
    public const uint FWP_BYTE_ARRAY16_TYPE = 11;
    public const uint FWP_BYTE_BLOB_TYPE = 12;
    public const uint FWP_SID = 13;
    public const uint FWP_SECURITY_DESCRIPTOR_TYPE = 14;
    public const uint FWP_TOKEN_INFORMATION_TYPE = 15;
    public const uint FWP_TOKEN_ACCESS_INFORMATION_TYPE = 16;
    public const uint FWP_UNICODE_STRING_TYPE = 17;
    public const uint FWP_BYTE_ARRAY6_TYPE = 18;
    public const uint FWP_SINGLE_DATA_TYPE_MAX = 0xFF;
    public const uint FWP_V4_ADDR_MASK = 0x100;
    public const uint FWP_V6_ADDR_MASK = 0x101;
    public const uint FWP_RANGE_TYPE = 0x102;
    public const uint FWP_DATA_TYPE_MAX = 0x103;
}
