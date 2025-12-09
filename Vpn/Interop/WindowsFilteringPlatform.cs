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

    // FWPM_FILTER0 - using Sequential layout matching Windows SDK definition
    // The struct must be marshaled to unmanaged memory manually for proper alignment
    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER0
    {
        public Guid filterKey;                    // 16 bytes
        public FWPM_DISPLAY_DATA0 displayData;    // 16 bytes (two pointers)
        public uint flags;                        // 4 bytes
        public IntPtr providerKey;                // 8 bytes (pointer to GUID)
        public FWP_BYTE_BLOB providerData;        // 16 bytes
        public Guid layerKey;                     // 16 bytes
        public Guid subLayerKey;                  // 16 bytes
        public FWP_VALUE0 weight;                 // 16 bytes
        public uint numFilterConditions;          // 4 bytes
        public IntPtr filterCondition;            // 8 bytes (pointer to array)
        public FWPM_ACTION0 action;               // 20 bytes (4 + 16)
        public ulong rawContext;                  // 8 bytes
        public IntPtr reserved;                   // 8 bytes
        public ulong filterId;                    // 8 bytes
        public FWP_VALUE0 effectiveWeight;        // 16 bytes
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

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;                       // 16 bytes
        public uint matchType;                      // 4 bytes + 4 padding
        public FWP_CONDITION_VALUE0 conditionValue; // 16 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_CONDITION_VALUE0
    {
        public uint type;       // 4 bytes + 4 padding
        public IntPtr value;    // 8 bytes (union - use IntPtr for pointer types)
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
