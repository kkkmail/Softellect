using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace Softellect.Vpn.Interop;

/// <summary>
/// Implements the VPN kill-switch using Windows Filtering Platform.
/// When enabled, blocks all traffic except to allowed destinations.
/// </summary>
public sealed class KillSwitch : IDisposable
{
    private IntPtr _engineHandle;
    private readonly List<ulong> _filterIds = new();
    private bool _disposed;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Enables the kill-switch, blocking all traffic except to allowed destinations.
    /// </summary>
    /// <param name="vpnServerIp">VPN server IP to allow.</param>
    /// <param name="vpnServerPort">VPN server port.</param>
    /// <param name="localLanExclusions">Local LAN subnets to allow (CIDR notation).</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Unit> Enable(IPAddress vpnServerIp, int vpnServerPort, IEnumerable<string> localLanExclusions)
    {
        if (_isEnabled)
        {
            return Result<Unit>.Failure("Kill-switch is already enabled");
        }

        // Create a dynamic session - filters will be automatically removed when the session ends
        // This also helps with permissions when running as a service
        var session = new WindowsFilteringPlatform.FWPM_SESSION0
        {
            displayData = new WindowsFilteringPlatform.FWPM_DISPLAY_DATA0
            {
                name = "Softellect VPN Kill-Switch Session",
                description = "Dynamic session for VPN kill-switch"
            },
            flags = WindowsFilteringPlatform.FWPM_SESSION_FLAG_DYNAMIC,
            txnWaitTimeoutInMSec = 0 // Infinite wait
        };

        var sessionPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsFilteringPlatform.FWPM_SESSION0>());
        Marshal.StructureToPtr(session, sessionPtr, false);

        try
        {
            // Open WFP engine with dynamic session
            var result = WindowsFilteringPlatform.FwpmEngineOpen0(
                null,
                WindowsFilteringPlatform.RPC_C_AUTHN_DEFAULT,
                IntPtr.Zero,
                sessionPtr,
                out _engineHandle);

            if (result != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to open WFP engine: 0x{result:X8}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sessionPtr);
        }

        try
        {
            // Begin transaction
            var txnResult = WindowsFilteringPlatform.FwpmTransactionBegin0(_engineHandle, 0);
            if (txnResult != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to begin transaction: 0x{txnResult:X8}");
            }

            // Add sublayer
            var addSublayerResult = AddSublayer();
            if (!addSublayerResult.IsSuccess)
            {
                WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
                return addSublayerResult;
            }

            // Add permit filter for loopback
            var loopbackResult = AddPermitFilter("127.0.0.0", 8, "Permit Loopback");
            if (!loopbackResult.IsSuccess)
            {
                WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
                return loopbackResult;
            }

            // Add permit filter for VPN server
            var vpnServerResult = AddPermitFilterForHost(vpnServerIp, "Permit VPN Server");
            if (!vpnServerResult.IsSuccess)
            {
                WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
                return vpnServerResult;
            }

            // Add permit filters for local LAN exclusions
            foreach (var exclusion in localLanExclusions)
            {
                var parts = exclusion.Split('/');
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out _) && int.TryParse(parts[1], out var prefix))
                {
                    var lanResult = AddPermitFilter(parts[0], prefix, $"Permit LAN {exclusion}");
                    if (!lanResult.IsSuccess)
                    {
                        WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
                        return lanResult;
                    }
                }
            }

            // Add block-all filter with lower weight (processes last)
            var blockResult = AddBlockAllFilter();
            if (!blockResult.IsSuccess)
            {
                WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
                return blockResult;
            }

            // Commit transaction
            var commitResult = WindowsFilteringPlatform.FwpmTransactionCommit0(_engineHandle);
            if (commitResult != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to commit transaction: 0x{commitResult:X8}");
            }

            _isEnabled = true;
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
            return Result<Unit>.Failure($"Exception enabling kill-switch: {ex.Message}");
        }
    }

    /// <summary>
    /// Disables the kill-switch, restoring normal traffic flow.
    /// </summary>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Unit> Disable()
    {
        if (!_isEnabled || _engineHandle == IntPtr.Zero)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        try
        {
            // Begin transaction
            var result = WindowsFilteringPlatform.FwpmTransactionBegin0(_engineHandle, 0);
            if (result != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to begin transaction: 0x{result:X8}");
            }

            // Remove all filters
            foreach (var filterId in _filterIds)
            {
                WindowsFilteringPlatform.FwpmFilterDeleteById0(_engineHandle, filterId);
            }
            _filterIds.Clear();

            // Remove sublayer
            var sublayerKey = WindowsFilteringPlatform.VpnSublayerGuid;
            WindowsFilteringPlatform.FwpmSubLayerDeleteByKey0(_engineHandle, ref sublayerKey);

            // Commit transaction
            result = WindowsFilteringPlatform.FwpmTransactionCommit0(_engineHandle);
            if (result != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to commit transaction: 0x{result:X8}");
            }

            _isEnabled = false;
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
            return Result<Unit>.Failure($"Exception disabling kill-switch: {ex.Message}");
        }
    }

    private Result<Unit> AddSublayer()
    {
        var sublayer = new WindowsFilteringPlatform.FWPM_SUBLAYER0
        {
            subLayerKey = WindowsFilteringPlatform.VpnSublayerGuid,
            displayData = new WindowsFilteringPlatform.FWPM_DISPLAY_DATA0
            {
                name = "Softellect VPN Kill-Switch",
                description = "Sublayer for VPN kill-switch filters"
            },
            weight = 0xFFFF // Highest priority
        };

        var result = WindowsFilteringPlatform.FwpmSubLayerAdd0(_engineHandle, ref sublayer, IntPtr.Zero);

        if (result != WindowsFilteringPlatform.ErrorSuccess && result != WindowsFilteringPlatform.FwpEAlreadyExists)
        {
            return Result<Unit>.Failure($"Failed to add sublayer: 0x{result:X8}");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private Result<Unit> AddPermitFilter(string network, int prefixLength, string name)
    {
        var ip = IPAddress.Parse(network);
        var ipUint = IPv4ToUInt32(ip);
        var mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);

        return AddFilterWithCondition(name, WindowsFilteringPlatform.FWP_ACTION_PERMIT, WindowsFilteringPlatform.FWPM_CONDITION_IP_REMOTE_ADDRESS, ipUint, mask, 100);
    }

    private Result<Unit> AddPermitFilterForHost(IPAddress ip, string name)
    {
        var ipUint = IPv4ToUInt32(ip);

        return AddFilterWithCondition(name, WindowsFilteringPlatform.FWP_ACTION_PERMIT, WindowsFilteringPlatform.FWPM_CONDITION_IP_REMOTE_ADDRESS, ipUint, uint.MaxValue, 100);
    }

    /// <summary>
    /// Adds a permit filter for traffic originating from the specified local IPv4 address.
    /// </summary>
    /// <param name="localIp">The local IP address to permit.</param>
    /// <param name="name">Display name for the filter.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Unit> AddPermitFilterForLocalHost(IPAddress localIp, string name)
    {
        if (_engineHandle == IntPtr.Zero || !_isEnabled)
        {
            return Result<Unit>.Failure("Kill-switch is not enabled");
        }

        try
        {
            var txnResult = WindowsFilteringPlatform.FwpmTransactionBegin0(_engineHandle, 0);
            if (txnResult != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to begin transaction: 0x{txnResult:X8}");
            }

            var ipUint = IPv4ToUInt32(localIp);
            
            var filterResult = AddFilterWithCondition(
                name,
                WindowsFilteringPlatform.FWP_ACTION_PERMIT,
                WindowsFilteringPlatform.FWPM_CONDITION_IP_LOCAL_ADDRESS,
                ipUint,
                uint.MaxValue,
                110);

            if (!filterResult.IsSuccess)
            {
                WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
                return filterResult;
            }

            var commitResult = WindowsFilteringPlatform.FwpmTransactionCommit0(_engineHandle);
            if (commitResult != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to commit transaction: 0x{commitResult:X8}");
            }

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            WindowsFilteringPlatform.FwpmTransactionAbort0(_engineHandle);
            return Result<Unit>.Failure($"Exception adding local host permit filter: {ex.Message}");
        }
    }

    private Result<Unit> AddBlockAllFilter()
    {
        var namePtr = Marshal.StringToHGlobalUni("VPN Kill-Switch Block All");
        var descPtr = Marshal.StringToHGlobalUni("Blocks all outbound traffic not explicitly permitted");

        try
        {
            // Manually marshal FWPM_FILTER0 to ensure correct layout
            // Total size: 192 bytes on x64
            var filterSize = 192;
            var filterPtr = Marshal.AllocHGlobal(filterSize);

            // Zero out all memory first (critical for WFP - reserved fields must be zero)
            for (int i = 0; i < filterSize; i++)
                Marshal.WriteByte(filterPtr, i, 0);

            var filterKey = Guid.NewGuid();

            // filterKey (GUID) at offset 0
            var filterKeyBytes = filterKey.ToByteArray();
            Marshal.Copy(filterKeyBytes, 0, filterPtr, 16);

            // displayData.name (LPWSTR) at offset 16
            Marshal.WriteIntPtr(filterPtr + 16, namePtr);

            // displayData.description (LPWSTR) at offset 24
            Marshal.WriteIntPtr(filterPtr + 24, descPtr);

            // flags (UINT32) at offset 32 - leave as 0
            // providerKey (GUID*) at offset 40 - leave as NULL
            // providerData.size at offset 48 - leave as 0
            // providerData.data at offset 56 - leave as NULL

            // layerKey (GUID) at offset 64
            var layerKeyBytes = WindowsFilteringPlatform.FWPM_LAYER_ALE_AUTH_CONNECT_V4.ToByteArray();
            Marshal.Copy(layerKeyBytes, 0, filterPtr + 64, 16);

            // subLayerKey (GUID) at offset 80
            var subLayerKeyBytes = WindowsFilteringPlatform.VpnSublayerGuid.ToByteArray();
            Marshal.Copy(subLayerKeyBytes, 0, filterPtr + 80, 16);

            // weight.type (UINT32) at offset 96 - FWP_EMPTY for auto-weight
            Marshal.WriteInt32(filterPtr + 96, (int)WindowsFilteringPlatform.FWP_EMPTY);

            // weight.value at offset 104 - leave as 0

            // numFilterConditions (UINT32) at offset 112 - 0 for block all
            Marshal.WriteInt32(filterPtr + 112, 0);

            // filterCondition (pointer) at offset 120 - NULL for block all
            Marshal.WriteIntPtr(filterPtr + 120, IntPtr.Zero);

            // action.type (UINT32) at offset 128
            Marshal.WriteInt32(filterPtr + 128, (int)WindowsFilteringPlatform.FWP_ACTION_BLOCK);

            // action.filterType (GUID) at offset 132 - leave as zero GUID
            // rawContext at offset 152 - leave as 0
            // reserved at offset 160 - leave as NULL
            // filterId at offset 168 - leave as 0 (assigned by system)
            // effectiveWeight at offset 176 - leave as 0 (assigned by system)

            try
            {
                var result = WindowsFilteringPlatform.FwpmFilterAdd0(_engineHandle, filterPtr, IntPtr.Zero, out var filterId);

                if (result != WindowsFilteringPlatform.ErrorSuccess)
                {
                    return Result<Unit>.Failure($"Failed to add block-all filter: 0x{result:X8}");
                }

                _filterIds.Add(filterId);
                return Result<Unit>.Success(Unit.Value);
            }
            finally
            {
                Marshal.FreeHGlobal(filterPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
            Marshal.FreeHGlobal(descPtr);
        }
    }

    private Result<Unit> AddFilterWithCondition(string name, uint action, Guid fieldKey, uint ipAddress, uint mask, byte filterWeight)
    {
        // Allocate condition value (FWP_V4_ADDR_AND_MASK)
        var addrMask = new WindowsFilteringPlatform.FWP_V4_ADDR_AND_MASK
        {
            addr = ipAddress,
            mask = mask
        };

        var addrMaskPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsFilteringPlatform.FWP_V4_ADDR_AND_MASK>());
        Marshal.StructureToPtr(addrMask, addrMaskPtr, false);

        var namePtr = Marshal.StringToHGlobalUni(name);
        var descPtr = Marshal.StringToHGlobalUni($"VPN Kill-Switch: {name}");

        try
        {
            // Manually marshal FWPM_FILTER_CONDITION0 to ensure correct layout
            // Total size: 40 bytes on x64
            var conditionSize = 40;
            var conditionPtr = Marshal.AllocHGlobal(conditionSize);

            // Zero out the memory first
            for (int i = 0; i < conditionSize; i++)
                Marshal.WriteByte(conditionPtr, i, 0);

            // fieldKey (GUID) at offset 0 - 16 bytes
            var fieldKeyBytes = fieldKey.ToByteArray();
            Marshal.Copy(fieldKeyBytes, 0, conditionPtr, 16);

            // matchType (UINT32) at offset 16
            Marshal.WriteInt32(conditionPtr + 16, (int)WindowsFilteringPlatform.FWP_MATCH_EQUAL);

            // conditionValue.type (UINT32) at offset 24
            Marshal.WriteInt32(conditionPtr + 24, (int)WindowsFilteringPlatform.FWP_V4_ADDR_MASK);

            // conditionValue.value (pointer) at offset 32
            Marshal.WriteIntPtr(conditionPtr + 32, addrMaskPtr);

            try
            {
                // Manually marshal FWPM_FILTER0 to ensure correct layout
                // Total size: 192 bytes on x64
                var filterSize = 192;
                var filterPtr = Marshal.AllocHGlobal(filterSize);

                // Zero out all memory first (critical for WFP - reserved fields must be zero)
                for (int i = 0; i < filterSize; i++)
                    Marshal.WriteByte(filterPtr, i, 0);

                var filterKey = Guid.NewGuid();

                // filterKey (GUID) at offset 0
                var filterKeyBytes = filterKey.ToByteArray();
                Marshal.Copy(filterKeyBytes, 0, filterPtr, 16);

                // displayData.name (LPWSTR) at offset 16
                Marshal.WriteIntPtr(filterPtr + 16, namePtr);

                // displayData.description (LPWSTR) at offset 24
                Marshal.WriteIntPtr(filterPtr + 24, descPtr);

                // flags (UINT32) at offset 32 - leave as 0

                // providerKey (GUID*) at offset 40 - leave as NULL

                // providerData.size at offset 48 - leave as 0
                // providerData.data at offset 56 - leave as NULL

                // layerKey (GUID) at offset 64
                var layerKeyBytes = WindowsFilteringPlatform.FWPM_LAYER_ALE_AUTH_CONNECT_V4.ToByteArray();
                Marshal.Copy(layerKeyBytes, 0, filterPtr + 64, 16);

                // subLayerKey (GUID) at offset 80
                var subLayerKeyBytes = WindowsFilteringPlatform.VpnSublayerGuid.ToByteArray();
                Marshal.Copy(subLayerKeyBytes, 0, filterPtr + 80, 16);

                // weight.type (UINT32) at offset 96
                Marshal.WriteInt32(filterPtr + 96, (int)WindowsFilteringPlatform.FWP_UINT8);

                // weight.value (union, 8 bytes) at offset 104 - for FWP_UINT8, value is in first byte
                Marshal.WriteByte(filterPtr + 104, (byte)Math.Min(filterWeight, (byte)15));

                // numFilterConditions (UINT32) at offset 112
                Marshal.WriteInt32(filterPtr + 112, 1);

                // filterCondition (pointer) at offset 120
                Marshal.WriteIntPtr(filterPtr + 120, conditionPtr);

                // action.type (UINT32) at offset 128
                Marshal.WriteInt32(filterPtr + 128, (int)action);

                // action.filterType (GUID) at offset 132 - leave as zero GUID

                // rawContext at offset 152 - leave as 0
                // reserved at offset 160 - leave as NULL
                // filterId at offset 168 - leave as 0 (assigned by system)
                // effectiveWeight at offset 176 - leave as 0 (assigned by system)

                try
                {
                    var result = WindowsFilteringPlatform.FwpmFilterAdd0(_engineHandle, filterPtr, IntPtr.Zero, out var filterId);

                    if (result != WindowsFilteringPlatform.ErrorSuccess)
                    {
                        return Result<Unit>.Failure($"Failed to add filter '{name}': 0x{result:X8}");
                    }

                    _filterIds.Add(filterId);
                    return Result<Unit>.Success(Unit.Value);
                }
                finally
                {
                    Marshal.FreeHGlobal(filterPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(conditionPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(addrMaskPtr);
            Marshal.FreeHGlobal(namePtr);
            Marshal.FreeHGlobal(descPtr);
        }
    }
    
    private static uint IPv4ToUInt32(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            throw new ArgumentException("Only IPv4 addresses are supported.", nameof(ip));

        // Windows is little-endian; WFP expects the v4 addr in native order.
        return BitConverter.ToUInt32(bytes, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isEnabled)
        {
            Disable();
        }

        if (_engineHandle != IntPtr.Zero)
        {
            WindowsFilteringPlatform.FwpmEngineClose0(_engineHandle);
            _engineHandle = IntPtr.Zero;
        }

        _disposed = true;
    }
}
