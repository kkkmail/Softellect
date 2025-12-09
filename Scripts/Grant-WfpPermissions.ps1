function Grant-WfpPermissions {
    <#
    .SYNOPSIS
        Grants Windows Filtering Platform (WFP) permissions to LOCAL SERVICE account.

    .DESCRIPTION
        This function grants the NT AUTHORITY\LOCAL SERVICE account the necessary
        permissions to interact with the Windows Filtering Platform (WFP) for
        implementing firewall rules and kill-switch functionality.

        WFP operations require access to the Base Filtering Engine (BFE) service.
        By default, LOCAL SERVICE does not have sufficient permissions to add
        or modify WFP filters.

    .EXAMPLE
        Grant-WfpPermissions

        Grants WFP permissions to LOCAL SERVICE account.

    .NOTES
        Requires administrator privileges.
        Must be run before starting a service that uses WFP under LOCAL SERVICE.
    #>
    [CmdletBinding()]
    param ()

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"
    . "$scriptDirectory\Test-AdminRights.ps1"

    # Check for admin rights
    if (-not (Test-AdminRights)) {
        Write-ServiceLog -Message "This function requires administrator privileges to run." -Level "Error"
        throw "Administrator privileges required."
    }

    $localServiceName = "NT AUTHORITY\LOCAL SERVICE"

    try {
        # Get LOCAL SERVICE SID
        $localServiceSid = (New-Object System.Security.Principal.NTAccount($localServiceName)).Translate([System.Security.Principal.SecurityIdentifier]).Value
        Write-ServiceLog -Message "LOCAL SERVICE SID: $localServiceSid"
    } catch {
        Write-ServiceLog -Message "Failed to get LOCAL SERVICE SID: $_" -Level "Error"
        throw
    }

    # Grant permissions to the BFE (Base Filtering Engine) service
    # BFE is the core service that manages WFP
    try {
        Write-ServiceLog -Message "Configuring BFE service permissions..."

        # Get current BFE service security descriptor
        $currentSddl = (& sc.exe sdshow bfe 2>&1).Trim()

        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentSddl)) {
            Write-ServiceLog -Message "Failed to get BFE service security descriptor. Is BFE service running?" -Level "Warning"
        } else {
            Write-ServiceLog -Message "Current BFE SDDL: $currentSddl"

            # Check if LOCAL SERVICE already has permissions
            if ($currentSddl -notlike "*$localServiceSid*") {
                # Add LOCAL SERVICE permissions to BFE
                # CCLCSWRPWPDTLOCRRC grants:
                # CC = Query service configuration
                # LC = Query service status
                # SW = Enumerate dependent services
                # RP = Start service
                # WP = Stop service
                # DT = Pause/continue service
                # LO = Query service lock status
                # CR = Query security descriptor
                # RC = Read control
                $newAce = "(A;;CCLCSWRPWPDTLOCRRC;;;$localServiceSid)"

                # Insert new ACE into DACL
                if ($currentSddl -match "D:") {
                    $newSddl = $currentSddl -replace "D:", "D:$newAce"
                } else {
                    $newSddl = "D:$newAce" + $currentSddl
                }

                Write-ServiceLog -Message "New BFE SDDL: $newSddl"

                # Apply new permissions
                $result = & sc.exe sdset bfe $newSddl 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-ServiceLog -Message "Successfully granted BFE service permissions to LOCAL SERVICE"
                } else {
                    Write-ServiceLog -Message "Failed to set BFE permissions: $result" -Level "Warning"
                }
            } else {
                Write-ServiceLog -Message "LOCAL SERVICE already has BFE service permissions"
            }
        }
    } catch {
        Write-ServiceLog -Message "Failed to configure BFE service permissions: $_" -Level "Warning"
    }

    # Grant permissions to WFP registry keys
    # These keys store WFP configuration and filter definitions
    $wfpRegistryKeys = @(
        "HKLM:\SYSTEM\CurrentControlSet\Services\BFE",
        "HKLM:\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy"
    )

    foreach ($regKey in $wfpRegistryKeys) {
        if (Test-Path $regKey) {
            try {
                $localServiceAccount = New-Object System.Security.Principal.NTAccount($localServiceName)
                $acl = Get-Acl $regKey
                $rule = New-Object System.Security.AccessControl.RegistryAccessRule(
                    $localServiceAccount,
                    "FullControl",
                    "ContainerInherit,ObjectInherit",
                    "None",
                    "Allow"
                )
                $acl.AddAccessRule($rule)
                Set-Acl $regKey $acl
                Write-ServiceLog -Message "Successfully granted LOCAL SERVICE access to $regKey"
            } catch {
                Write-ServiceLog -Message "Failed to grant access to ${regKey}: $_" -Level "Warning"
            }
        } else {
            Write-ServiceLog -Message "Registry key does not exist: $regKey" -Level "Warning"
        }
    }

    # Grant the SeImpersonatePrivilege to LOCAL SERVICE
    # This privilege is sometimes needed for WFP operations
    try {
        Write-ServiceLog -Message "Granting SeImpersonatePrivilege to LOCAL SERVICE..."

        $tempInf = [System.IO.Path]::GetTempFileName() + ".inf"

        $infContent = @"
[Unicode]
Unicode=yes
[Version]
signature="`$CHICAGO`$"
Revision=1
[Privilege Rights]
SeImpersonatePrivilege = *S-1-5-19,*S-1-5-20,*S-1-5-6
"@

        Set-Content -Path $tempInf -Value $infContent -Encoding ASCII

        $tempDb = [System.IO.Path]::GetTempFileName() + ".sdb"
        $result = Start-Process -FilePath secedit.exe -ArgumentList "/configure /db `"$tempDb`" /cfg `"$tempInf`" /quiet" -Wait -NoNewWindow -PassThru

        if ($result.ExitCode -eq 0) {
            Write-ServiceLog -Message "Successfully granted SeImpersonatePrivilege to LOCAL SERVICE"
        } else {
            Write-ServiceLog -Message "SeImpersonatePrivilege grant completed with code $($result.ExitCode)"
        }

        # Clean up
        Remove-Item $tempInf -Force -ErrorAction SilentlyContinue
        Remove-Item $tempDb -Force -ErrorAction SilentlyContinue

    } catch {
        Write-ServiceLog -Message "Failed to grant SeImpersonatePrivilege: $_" -Level "Warning"
    }

    # Grant the SeSecurityPrivilege (needed for some WFP operations)
    try {
        Write-ServiceLog -Message "Granting SeSecurityPrivilege to LOCAL SERVICE..."

        $tempInf = [System.IO.Path]::GetTempFileName() + ".inf"

        # SeSecurityPrivilege allows managing auditing and security logs
        # This is sometimes required for WFP filter management
        $infContent = @"
[Unicode]
Unicode=yes
[Version]
signature="`$CHICAGO`$"
Revision=1
[Privilege Rights]
SeSecurityPrivilege = *S-1-5-19
"@

        Set-Content -Path $tempInf -Value $infContent -Encoding ASCII

        $tempDb = [System.IO.Path]::GetTempFileName() + ".sdb"
        $result = Start-Process -FilePath secedit.exe -ArgumentList "/configure /db `"$tempDb`" /cfg `"$tempInf`" /quiet" -Wait -NoNewWindow -PassThru

        if ($result.ExitCode -eq 0) {
            Write-ServiceLog -Message "Successfully granted SeSecurityPrivilege to LOCAL SERVICE"
        } else {
            Write-ServiceLog -Message "SeSecurityPrivilege grant completed with code $($result.ExitCode)"
        }

        # Clean up
        Remove-Item $tempInf -Force -ErrorAction SilentlyContinue
        Remove-Item $tempDb -Force -ErrorAction SilentlyContinue

    } catch {
        Write-ServiceLog -Message "Failed to grant SeSecurityPrivilege: $_" -Level "Warning"
    }

    Write-ServiceLog -Message "WFP permission granting process completed"
    Write-ServiceLog -Message "NOTE: You may need to restart the BFE service or reboot for changes to take effect" -Level "Warning"
}
