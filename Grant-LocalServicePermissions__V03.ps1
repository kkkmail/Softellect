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

# Grant service management permissions for the specific service
$serviceName = "WorkerNodeService-1"  # Update this if it's dynamic
try {
    # Main services key
    $serviceConfigManagerKey = "HKLM:\SYSTEM\CurrentControlSet\Services"
    $acl = Get-Acl $serviceConfigManagerKey
    $rule = New-Object System.Security.AccessControl.RegistryAccessRule(
        $localServiceAccount,
        "FullControl",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.SetAccessRule($rule)
    Set-Acl $serviceConfigManagerKey $acl
    Write-Host "Successfully granted LOCAL SERVICE access to service configuration"

    # Grant permissions to the specific service key
    $specificServiceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    if (Test-Path $specificServiceKey) {
        $serviceAcl = Get-Acl $specificServiceKey
        $serviceRule = New-Object System.Security.AccessControl.RegistryAccessRule(
            $localServiceAccount,
            "FullControl",
            "ContainerInherit,ObjectInherit",
            "None",
            "Allow"
        )
        $serviceAcl.SetAccessRule($serviceRule)
        Set-Acl $specificServiceKey $serviceAcl
        Write-Host "Successfully granted LOCAL SERVICE access to $serviceName service configuration"
    }

    # More importantly, grant service control permissions
    # Using the sc.exe utility to grant permissions
    $sid = (New-Object System.Security.Principal.NTAccount($localServiceName)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    $sddl = "D:(A;;RPWPCCDCLCSWRCWDWOGA;;;$sid)"  # Full control for LOCAL SERVICE
    & sc.exe sdset $serviceName "$sddl"
    Write-Host "Successfully granted service control permissions to LOCAL SERVICE for $serviceName"

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
        $folderAcl.SetAccessRule($folderPermission)
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
        if (Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue) {
            Add-LocalGroupMember -Group $groupName -Member $localServiceName -ErrorAction Stop
            Write-Host "Successfully added LOCAL SERVICE to $groupName group"
        } else {
            Write-Host "Group $groupName does not exist on this system"
        }
    } catch {
        if ($_.Exception.Message -like "*already a member*") {
            Write-Host "LOCAL SERVICE is already a member of $groupName group"
        } else {
            Write-Warning "Could not add LOCAL SERVICE to $groupName group: $_"
        }
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
