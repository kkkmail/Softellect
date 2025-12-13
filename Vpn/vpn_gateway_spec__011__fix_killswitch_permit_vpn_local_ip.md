# vpn_gateway_spec__011__fix_killswitch_permit_vpn_local_ip.md

## Purpose

Fix the current behavior where DNS may work but general internet traffic does not, due to the client kill-switch blocking outbound connections to arbitrary remote internet IPs.

The kill-switch currently permits only:
- loopback
- VPN server public IP (remote address)
- optional LAN exclusions (remote address)
and then blocks everything else.

We need to permit outbound connects when the connection is routed through the VPN adapter by permitting traffic whose **local IPv4 address equals the assigned VPN IP** (10.66.77.x).

## Working directory

`C:\GitHub\Softellect\Vpn\`

## Rules for Claude Code (must follow)

- **Do not produce duplicate code.** Reuse existing helpers; refactor only as needed.
- **F# naming:** use **camelCase** for bindings, functions, and members (as in existing code).
- **C# naming:** follow existing style in the file.
- If anything in these instructions appears inconsistent with the current code, **stop and ask for confirmation** instead of guessing.
- Do not touch `.git\*`.

## Files to change

1) `C:\GitHub\Softellect\Vpn\Interop\KillSwitch.cs`
2) `C:\GitHub\Softellect\Vpn\Client\Service.fs`

Do not change any other files.

---

## Change 1: Interop/KillSwitch.cs

### 1.1 Generalize the filter condition field key (local vs remote address)

Currently the code that builds the WFP condition uses only:

- `WindowsFilteringPlatform.FWPM_CONDITION_IP_REMOTE_ADDRESS`

Refactor the internal helper so it can be used for both remote and local address conditions.

**Required change:**

- Change `AddFilterWithCondition(...)` to accept a `Guid fieldKey` argument and use it when marshalling `FWPM_FILTER_CONDITION0.fieldKey`.

Target signature:

```csharp
private Result<Unit> AddFilterWithCondition(string name, uint action, Guid fieldKey, uint ipAddress, uint mask, byte filterWeight)
```

Then:
- Replace the hardcoded remote-address field key with the passed-in `fieldKey`.

### 1.2 Update existing permit helpers (no behavior change)

Update these methods to pass `WindowsFilteringPlatform.FWPM_CONDITION_IP_REMOTE_ADDRESS`:

- `AddPermitFilter(string network, int prefixLength, string name)`
- `AddPermitFilterForHost(IPAddress ip, string name)`

No logic changes besides calling the new signature.

### 1.3 Add a new public method: permit by **local** IPv4 address

Add this method to `KillSwitch`:

```csharp
public Result<Unit> AddPermitFilterForLocalHost(IPAddress localIp, string name)
```

Behavior requirements:

- If `_engineHandle == IntPtr.Zero` or `_isEnabled == false`, return failure:
  - `"Kill-switch is not enabled"`
- Begin a WFP transaction (`FwpmTransactionBegin0`).
- Add a PERMIT filter using the generalized helper with:
  - `fieldKey = WindowsFilteringPlatform.FWPM_CONDITION_IP_LOCAL_ADDRESS`
  - `ipAddress = localIp` converted to uint32 in network byte order (same conversion style already used)
  - `mask = uint.MaxValue` (i.e., /32)
  - `filterWeight = 110`
- Commit the transaction (`FwpmTransactionCommit0`).
- On error: abort the transaction and return failure with a message consistent with the file style.
- This method must **only add** the new permit filter; do not remove/replace any existing filters.

Note: The helper already tracks `filterId` in `_filterIds`. Ensure this new permit filter is tracked the same way.

---

## Change 2: Client/Service.fs

File: `C:\GitHub\Softellect\Vpn\Client\Service.fs`

### 2.1 After tunnel is started, permit the assigned VPN local IP

In `IHostedService.StartAsync`, keep the existing order:
1) enable kill-switch
2) authenticate
3) start tunnel

Immediately after `startTunnel assignedIp` succeeds, and **before**:
- `running <- true`
- starting the send/receive threads

Add:

- Convert `assignedIp` (F# `IpAddress` wrapper) to `System.Net.IPAddress` at the interop boundary:

```fs
let assignedIpAddress = System.Net.IPAddress.Parse(assignedIp.value)
```

- Call the new C# interop method:

```fs
match killSwitch with
| Some ks ->
    let r = ks.AddPermitFilterForLocalHost(assignedIpAddress, $"Permit VPN Local {assignedIp.value}")
    if r.IsSuccess then
        Logger.logInfo $"Kill-switch: permitted VPN local address {assignedIp.value}"
    else
        let errMsg = match r.Error with | null -> "Unknown error" | e -> e
        state <- Failed errMsg
        Logger.logError $"Failed to permit VPN local address in kill-switch: {errMsg}"
        Task.CompletedTask
| None ->
    state <- Failed "Kill-switch is not enabled"
    Logger.logError "Kill-switch instance is missing after Enable()"
    Task.CompletedTask
```

Important:
- Use **camelCase** (`assignedIpAddress`, `errMsg`) to match existing F# style.
- This failure must be treated as fatal (do not start threads).

No other client changes.

---

## Acceptance criteria

After applying the changes:

- Client log shows:
  - "Kill-switch enabled"
  - "Tunnel started with IP: 10.66.77.x"
  - "Kill-switch: permitted VPN local address 10.66.77.x"
- Client tunnel logs show traffic to real internet IPs (not just DNS/NBNS).
- Server NAT outbound logs show packets from `10.66.77.x:<port>` being NAT-translated and sent out.
