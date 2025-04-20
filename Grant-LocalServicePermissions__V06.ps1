#Requires -RunAsAdministrator
param (
    [Parameter(Mandatory = $true)]
    [string]$FolderPath
)

# This script grants the LOCAL SERVICE account the necessary permissions
# to manage services and write to the specified folder

# The proper way to get the LOCAL SERVICE account
$localServiceName = "NT AUTHORITY\LOCAL SERVICE"
$localServiceAccount = New-Object System.Security.Principal.NTAccount($localServiceName)

try {
    # Grant permissions to the services registry key - adding, not replacing
    $serviceConfigManagerKey = "HKLM:\SYSTEM\CurrentControlSet\Services"
    $acl = Get-Acl $serviceConfigManagerKey
    $rule = New-Object System.Security.AccessControl.RegistryAccessRule(
        $localServiceAccount,
        "FullControl",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.AddAccessRule($rule)
    Set-Acl $serviceConfigManagerKey $acl
    Write-Host "Successfully granted LOCAL SERVICE access to service configuration"

    # Grant SCM permissions by adding LOCAL SERVICE to the administrators group
    # This is simpler and more reliable than modifying the SDDL
    # The group membership is already handled below, so we just ensure it happens
    Write-Host "Granting service control manager permissions through group membership"

} catch {
    Write-Warning "Failed to grant access to service configuration: $_"
}

# Grant folder access permissions
if (Test-Path -Path $FolderPath) {
    try {
        $folderAcl = Get-Acl $FolderPath
        $folderPermission = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $localServiceAccount,
            "FullControl",
            "ContainerInherit,ObjectInherit",
            "None",
            "Allow"
        )
        $folderAcl.AddAccessRule($folderPermission)
        Set-Acl $FolderPath $folderAcl
        
        Write-Host "Successfully granted LOCAL SERVICE account permissions to $FolderPath"
    } catch {
        Write-Warning "Failed to grant folder permissions: $_"
    }
} else {
    Write-Error "Folder path does not exist: $FolderPath"
}

# Check if the group exists before trying to add the account to it
$groups = @("Performance Log Users", "Performance Monitor Users", "Administrators")
foreach ($groupName in $groups) {
    try {
        # Try to get the group by SID for more reliable access
        $knownGroups = @{
            "Performance Log Users" = "S-1-5-32-559"
            "Performance Monitor Users" = "S-1-5-32-558"
            "Administrators" = "S-1-5-32-544"
        }
        
        $groupSid = $knownGroups[$groupName]
        try {
            $group = Get-LocalGroup -SID $groupSid -ErrorAction Stop
            Add-LocalGroupMember -SID $groupSid -Member $localServiceName -ErrorAction Stop
            Write-Host "Successfully added LOCAL SERVICE to $groupName group"
        } catch {
            if ($_.Exception.Message -like "*already a member*") {
                Write-Host "LOCAL SERVICE is already a member of $groupName group"
            } else {
                # Fall back to name-based search
                if (Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue) {
                    Add-LocalGroupMember -Group $groupName -Member $localServiceName -ErrorAction Stop
                    Write-Host "Successfully added LOCAL SERVICE to $groupName group"
                } else {
                    Write-Host "Group $groupName does not exist on this system"
                }
            }
        }
    } catch {
        Write-Warning "Could not add LOCAL SERVICE to $groupName group: $_"
    }
}

# As an alternative to modifying SCM SDDL, grant specific service rights
try {
    # Grant access to manage services through policy
    $seManageVolumePrivilege = "SeManageVolumePrivilege"
    $seServiceLogonRight = "SeServiceLogonRight"
    
    # Use secedit to grant rights
    $tempFile = [System.IO.Path]::GetTempFileName()
    
    # Export current security policy
    secedit /export /cfg $tempFile
    
    # Read the current policy
    $content = Get-Content $tempFile
    
    # Add LOCAL SERVICE to service logon right if not already there
    $found = $false
    $newContent = @()
    foreach ($line in $content) {
        if ($line -match "SeServiceLogonRight") {
            if ($line -notlike "*LOCAL SERVICE*") {
                $line = $line.TrimEnd() + ",*S-1-5-19"  # S-1-5-19 is the SID for LOCAL SERVICE
            }
            $found = $true
        }
        $newContent += $line
    }
    
    if (-not $found) {
        $newContent += "SeServiceLogonRight = *S-1-5-19"
    }
    
    # Write the modified policy back
    Set-Content -Path $tempFile -Value $newContent
    
    # Import the modified policy
    secedit /configure /db secedit.sdb /cfg $tempFile
    
    # Clean up
    Remove-Item $tempFile
    Remove-Item secedit.sdb
    
    Write-Host "Successfully granted service logon right to LOCAL SERVICE"
} catch {
    Write-Warning "Failed to grant service logon right via policy: $_"
}

Write-Host "Permission granting process completed"
