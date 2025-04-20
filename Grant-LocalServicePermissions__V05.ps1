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

    # Get the current SCM security descriptor and add LOCAL SERVICE permissions
    $sid = (New-Object System.Security.Principal.NTAccount($localServiceName)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    
    # Get current SCM permissions
    $currentSddl = (& sc.exe sdshow scmanager).Trim()
    
    # Parse the current SDDL to add our ACE
    if ($currentSddl) {
        # Check if LOCAL SERVICE already has permissions
        if ($currentSddl -notlike "*$sid*") {
            # Extract the existing DACL and add our ACE
            if ($currentSddl -match "D:(?:\(.*?\))+") {
                $dacl = $matches[0]
                $newAce = "(A;;CCLCRPWPDTLOCRSDRCWDWO;;;$sid)"
                
                # Insert our ACE right after "D:"
                $newDacl = $dacl.Replace("D:", "D:$newAce")
                $newSddl = $currentSddl.Replace($dacl, $newDacl)
                
                # Apply new SCM permissions
                Write-Host "Updating SCM permissions..."
                & sc.exe sdset scmanager $newSddl
                Write-Host "Successfully granted service control manager permissions to LOCAL SERVICE"
            } else {
                Write-Warning "Unable to parse current SDDL format"
            }
        } else {
            Write-Host "LOCAL SERVICE already has SCM permissions"
        }
    } else {
        Write-Warning "Unable to retrieve current SCM security descriptor"
    }

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

# Grant service logon right using ntrights if available
$ntrightsPath = "C:\Windows\System32\ntrights.exe"
if (Test-Path -Path $ntrightsPath) {
    try {
        & $ntrightsPath +r SeServiceLogonRight -u "NT AUTHORITY\LOCAL SERVICE"
        Write-Host "Successfully granted service logon right"
    } catch {
        Write-Warning "Failed to grant service logon right: $_"
    }
} else {
    Write-Host "NTRights utility not found, skipping service logon right grant"
}

Write-Host "Permission granting process completed"
