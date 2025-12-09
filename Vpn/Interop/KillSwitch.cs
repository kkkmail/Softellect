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

        // Open WFP engine
        var result = WindowsFilteringPlatform.FwpmEngineOpen0(
            null,
            WindowsFilteringPlatform.RPC_C_AUTHN_WINNT,
            IntPtr.Zero,
            IntPtr.Zero,
            out _engineHandle);

        if (result != WindowsFilteringPlatform.ErrorSuccess)
        {
            return Result<Unit>.Failure($"Failed to open WFP engine: 0x{result:X8}");
        }

        try
        {
            // Begin transaction
            result = WindowsFilteringPlatform.FwpmTransactionBegin0(_engineHandle, 0);
            if (result != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to begin transaction: 0x{result:X8}");
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
            result = WindowsFilteringPlatform.FwpmTransactionCommit0(_engineHandle);
            if (result != WindowsFilteringPlatform.ErrorSuccess)
            {
                return Result<Unit>.Failure($"Failed to commit transaction: 0x{result:X8}");
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
        var ipBytes = ip.GetAddressBytes();
        var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
        var mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);

        return AddFilterWithCondition(name, WindowsFilteringPlatform.FWP_ACTION_PERMIT, ipUint, mask, 100);
    }

    private Result<Unit> AddPermitFilterForHost(IPAddress ip, string name)
    {
        var ipBytes = ip.GetAddressBytes();
        var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);

        return AddFilterWithCondition(name, WindowsFilteringPlatform.FWP_ACTION_PERMIT, ipUint, uint.MaxValue, 100);
    }

    private Result<Unit> AddBlockAllFilter()
    {
        // Block all filter without conditions (matches everything)
        var filter = new WindowsFilteringPlatform.FWPM_FILTER0
        {
            filterKey = Guid.NewGuid(),
            displayData = new WindowsFilteringPlatform.FWPM_DISPLAY_DATA0
            {
                name = "VPN Kill-Switch Block All",
                description = "Blocks all outbound traffic not explicitly permitted"
            },
            layerKey = WindowsFilteringPlatform.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
            subLayerKey = WindowsFilteringPlatform.VpnSublayerGuid,
            weight = new WindowsFilteringPlatform.FWP_VALUE0
            {
                type = WindowsFilteringPlatform.FWP_UINT8,
                value = new WindowsFilteringPlatform.FWP_VALUE0_UNION { uint8 = 1 } // Low weight = processes last
            },
            numFilterConditions = 0,
            filterCondition = IntPtr.Zero,
            action = new WindowsFilteringPlatform.FWPM_ACTION0
            {
                type = WindowsFilteringPlatform.FWP_ACTION_BLOCK
            }
        };

        var result = WindowsFilteringPlatform.FwpmFilterAdd0(_engineHandle, ref filter, IntPtr.Zero, out var filterId);

        if (result != WindowsFilteringPlatform.ErrorSuccess)
        {
            return Result<Unit>.Failure($"Failed to add block-all filter: 0x{result:X8}");
        }

        _filterIds.Add(filterId);
        return Result<Unit>.Success(Unit.Value);
    }

    private Result<Unit> AddFilterWithCondition(string name, uint action, uint ipAddress, uint mask, byte weight)
    {
        // Allocate condition
        var addrMask = new WindowsFilteringPlatform.FWP_V4_ADDR_AND_MASK
        {
            addr = ipAddress,
            mask = mask
        };

        var addrMaskPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsFilteringPlatform.FWP_V4_ADDR_AND_MASK>());
        Marshal.StructureToPtr(addrMask, addrMaskPtr, false);

        try
        {
            var condition = new WindowsFilteringPlatform.FWPM_FILTER_CONDITION0
            {
                fieldKey = WindowsFilteringPlatform.FWPM_CONDITION_IP_REMOTE_ADDRESS,
                matchType = WindowsFilteringPlatform.FWP_MATCH_EQUAL,
                conditionValue = new WindowsFilteringPlatform.FWP_CONDITION_VALUE0
                {
                    type = WindowsFilteringPlatform.FWP_V4_ADDR_MASK,
                    value = new WindowsFilteringPlatform.FWP_CONDITION_VALUE0_UNION { v4AddrMask = addrMaskPtr }
                }
            };

            var conditionPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsFilteringPlatform.FWPM_FILTER_CONDITION0>());
            Marshal.StructureToPtr(condition, conditionPtr, false);

            try
            {
                var filter = new WindowsFilteringPlatform.FWPM_FILTER0
                {
                    filterKey = Guid.NewGuid(),
                    displayData = new WindowsFilteringPlatform.FWPM_DISPLAY_DATA0
                    {
                        name = name,
                        description = $"VPN Kill-Switch: {name}"
                    },
                    layerKey = WindowsFilteringPlatform.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                    subLayerKey = WindowsFilteringPlatform.VpnSublayerGuid,
                    weight = new WindowsFilteringPlatform.FWP_VALUE0
                    {
                        type = WindowsFilteringPlatform.FWP_UINT8,
                        value = new WindowsFilteringPlatform.FWP_VALUE0_UNION { uint8 = weight }
                    },
                    numFilterConditions = 1,
                    filterCondition = conditionPtr,
                    action = new WindowsFilteringPlatform.FWPM_ACTION0
                    {
                        type = action
                    }
                };

                var result = WindowsFilteringPlatform.FwpmFilterAdd0(_engineHandle, ref filter, IntPtr.Zero, out var filterId);

                if (result != WindowsFilteringPlatform.ErrorSuccess)
                {
                    return Result<Unit>.Failure($"Failed to add filter '{name}': 0x{result:X8}");
                }

                _filterIds.Add(filterId);
                return Result<Unit>.Success(Unit.Value);
            }
            finally
            {
                Marshal.FreeHGlobal(conditionPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(addrMaskPtr);
        }
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
