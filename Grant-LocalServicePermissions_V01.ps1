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

    # Grant SCM permissions using sc.exe
    $sid = (New-Object System.Security.Principal.NTAccount($localServiceName)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    
    # Get current SCM permissions
    $currentSddl = (& sc.exe sdshow scmanager).Trim()
    
    if ($currentSddl) {
        # Check if LOCAL SERVICE already has permissions
        if ($currentSddl -notlike "*$sid*") {
            # Add LOCAL SERVICE permissions to SCM
            # This SDDL grants all service management permissions:
            # CC = Connect to Service Control Manager
            # LC = Enumerate services
            # SW = Enumerate dependent services
            # LO = Query service's lock status
            # RP = Start service
            # WP = Stop service
            # DT = Pause/continue service
            # CR = Create service
            # SD = Delete service
            # RC = Query service's configuration
            # WO = Change service's configuration
            $newAce = "(A;;CCLCSWLORPWPDTCRSDRCWDWO;;;$sid)"
            
            # Insert LOCAL SERVICE permissions
            if ($currentSddl -match "D:") {
                $newSddl = $currentSddl -replace "D:", "D:$newAce"
            } else {
                $newSddl = "D:$newAce" + $currentSddl
            }
            
            # Apply new SCM permissions
            $result = & sc.exe sdset scmanager $newSddl
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Successfully granted service control manager permissions to LOCAL SERVICE"
            } else {
                Write-Warning "Failed to set SCM permissions. Exit code: $LASTEXITCODE"
            }
        } else {
            Write-Host "LOCAL SERVICE already has SCM permissions"
        }
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

# Add LOCAL SERVICE to important groups
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

# Grant service logon right using secedit
try {
    # Create a minimal policy file with just the service logon right
    $tempInf = [System.IO.Path]::GetTempFileName() + ".inf"
    
    # Create a minimal INF file content
    $infContent = @"
[Unicode]
Unicode=yes
[Version]
signature="`$CHICAGO$"
Revision=1
[Privilege Rights]
SeServiceLogonRight = *S-1-5-19
"@
    
    Set-Content -Path $tempInf -Value $infContent -Encoding ASCII
    
    # Import the policy
    $tempDb = [System.IO.Path]::GetTempFileName() + ".sdb"
    $result = Start-Process -FilePath secedit.exe -ArgumentList "/configure /db `"$tempDb`" /cfg `"$tempInf`" /quiet" -Wait -NoNewWindow -PassThru
    
    if ($result.ExitCode -eq 0) {
        Write-Host "Successfully granted service logon right to LOCAL SERVICE"
    } else {
        Write-Host "Service logon right grant completed with code $($result.ExitCode)"
    }
    
    # Clean up
    Remove-Item $tempInf -Force -ErrorAction SilentlyContinue
    Remove-Item $tempDb -Force -ErrorAction SilentlyContinue
    
} catch {
    Write-Warning "Failed to grant service logon right: $_"
}

Write-Host "Permission granting process completed"
