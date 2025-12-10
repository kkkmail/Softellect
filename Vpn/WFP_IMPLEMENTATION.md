# Windows Filtering Platform (WFP) Kill-Switch Implementation

This document describes the WFP implementation for the VPN kill-switch.

## Overview

The VPN client uses Windows Filtering Platform (WFP) to implement a kill-switch that blocks all network traffic except:
- Traffic to the VPN server
- Local loopback (127.0.0.0/8)
- Local LAN exclusions (configurable)

When the VPN disconnects, all traffic is blocked to prevent IP leaks.

## Architecture

### Key Files

- `Vpn\Interop\WindowsFilteringPlatform.cs` - P/Invoke definitions for WFP API
- `Vpn\Interop\KillSwitch.cs` - Kill-switch implementation using WFP
- `Scripts\Grant-WfpPermissions.ps1` - Script to grant WFP permissions to LOCAL SERVICE

### WFP Components Used

1. **Engine** - WFP engine handle obtained via `FwpmEngineOpen0`
2. **Sublayer** - Custom sublayer for VPN filters (`VpnSublayerGuid`)
3. **Filters** - Block/permit filters added to the sublayer
4. **Transactions** - All filter operations are wrapped in transactions

## P/Invoke Struct Definitions

### Critical Struct Layouts (x64)

**FWPM_FILTER0** - 192 bytes:
```
Offset  Size  Field
0       16    filterKey (GUID)
16      8     displayData.name (LPWSTR)
24      8     displayData.description (LPWSTR)
32      4     flags (UINT32)
40      8     providerKey (GUID*)
48      4     providerData.size
56      8     providerData.data (UINT8*)
64      16    layerKey (GUID)
80      16    subLayerKey (GUID)
96      4     weight.type (FWP_DATA_TYPE)
104     8     weight.value (union)
112     4     numFilterConditions (UINT32)
120     8     filterCondition (FWPM_FILTER_CONDITION0*)
128     4     action.type (FWP_ACTION_TYPE)
132     16    action.filterType (GUID)
152     8     rawContext (UINT64)
160     8     reserved (void*)
168     8     filterId (UINT64)
176     4     effectiveWeight.type
184     8     effectiveWeight.value
```

**FWPM_FILTER_CONDITION0** - 40 bytes:
```
Offset  Size  Field
0       16    fieldKey (GUID)
16      4     matchType (FWP_MATCH_TYPE)
24      4     conditionValue.type (FWP_DATA_TYPE)
32      8     conditionValue.value (union pointer)
```

### Constants

**Action Types** (FWP_ACTION_TYPE):
```csharp
FWP_ACTION_FLAG_TERMINATING = 0x00001000
FWP_ACTION_BLOCK = 0x0001 | FWP_ACTION_FLAG_TERMINATING  // = 0x1001
FWP_ACTION_PERMIT = 0x0002 | FWP_ACTION_FLAG_TERMINATING // = 0x1002
```

The terminating flag indicates that the action stops further filter evaluation in the sublayer.

**Data Types** (FWP_DATA_TYPE):
```csharp
FWP_EMPTY = 0
FWP_UINT8 = 1
FWP_UINT16 = 2
FWP_UINT32 = 3
FWP_UINT64 = 4
FWP_INT8 = 5
FWP_INT16 = 6
FWP_INT32 = 7
FWP_INT64 = 8
FWP_FLOAT = 9
FWP_DOUBLE = 10
FWP_BYTE_ARRAY16_TYPE = 11
FWP_BYTE_BLOB_TYPE = 12
FWP_SID = 13
FWP_SECURITY_DESCRIPTOR_TYPE = 14
FWP_TOKEN_INFORMATION_TYPE = 15
FWP_TOKEN_ACCESS_INFORMATION_TYPE = 16
FWP_UNICODE_STRING_TYPE = 17
FWP_BYTE_ARRAY6_TYPE = 18
FWP_SINGLE_DATA_TYPE_MAX = 0xFF
FWP_V4_ADDR_MASK = 0x100   // 256
FWP_V6_ADDR_MASK = 0x101   // 257
FWP_RANGE_TYPE = 0x102     // 258
FWP_DATA_TYPE_MAX = 0x103  // 259
```

**Match Types** (FWP_MATCH_TYPE):
```csharp
FWP_MATCH_EQUAL = 0
FWP_MATCH_GREATER = 1
FWP_MATCH_LESS = 2
FWP_MATCH_GREATER_OR_EQUAL = 3
FWP_MATCH_LESS_OR_EQUAL = 4
FWP_MATCH_RANGE = 5
FWP_MATCH_FLAGS_ALL_SET = 6
FWP_MATCH_FLAGS_ANY_SET = 7
FWP_MATCH_FLAGS_NONE_SET = 8
FWP_MATCH_EQUAL_CASE_INSENSITIVE = 9
FWP_MATCH_NOT_EQUAL = 10
FWP_MATCH_TYPE_MAX = 11
```

## Running as Windows Service

### Required Permissions

When running under `NT AUTHORITY\LOCAL SERVICE`, the following permissions are needed:

1. **BFE Service Access** - Access to Base Filtering Engine
2. **Registry Permissions** - Read/write access to WFP registry keys
3. **Security Privileges** - SeImpersonatePrivilege, SeSecurityPrivilege

### Grant Permissions Script

The `Grant-WfpPermissions.ps1` script (in Scripts folder) grants these permissions:

```powershell
. .\Grant-WfpPermissions.ps1
Grant-WfpPermissions
```

After granting permissions, restart the BFE service:
```powershell
Restart-Service -Name BFE -Force
```

## Dynamic Sessions

The kill-switch uses dynamic WFP sessions (`FWPM_SESSION_FLAG_DYNAMIC`). Benefits:
- Filters are automatically removed when the session ends (process exits)
- Reduces permission requirements
- Cleaner cleanup on crash

## Filter Weight and Priority

- Permit filters (loopback, VPN server, LAN exclusions) have higher weight
- Block-all filter has `FWP_EMPTY` weight type (auto-assigned, lowest priority)
- Higher weight filters are evaluated first
- First matching terminating filter ends evaluation

## Troubleshooting

### WFP Error 0x00000005 (ACCESS_DENIED)

Service account lacks WFP permissions. Run `Grant-WfpPermissions` as Administrator and restart the BFE service.

### WFP Error 0x80320024 (FWP_E_INVALID_ACTION_TYPE)

Action type must include `FWP_ACTION_FLAG_TERMINATING` (0x1000). Use 0x1001 for BLOCK and 0x1002 for PERMIT.

## References

- [WFP Error Codes](https://learn.microsoft.com/en-us/windows/win32/fwp/wfp-error-codes)
- [FWPM_FILTER0 Structure](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_filter0)
- [FWPM_ACTION0 Structure](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_action0)
- [FwpmFilterAdd0 Function](https://learn.microsoft.com/en-us/windows/win32/api/fwpmu/nf-fwpmu-fwpmfilteradd0)
- [Filter Arbitration](https://learn.microsoft.com/en-us/windows/win32/fwp/filter-arbitration)
