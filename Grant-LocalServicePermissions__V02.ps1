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

# Grant service management permissions
try {
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

# Add LOCAL SERVICE to Performance Log Users group for service control
try {
    Add-LocalGroupMember -Group "Performance Log Users" -Member $localServiceName -ErrorAction Stop
    Write-Host "Successfully added LOCAL SERVICE to Performance Log Users group"
} catch {
    if ($_.Exception.Message -notlike "*already a member*") {
        Write-Warning "Could not add LOCAL SERVICE to Performance Log Users group: $_"
    } else {
        Write-Host "LOCAL SERVICE is already a member of Performance Log Users group"
    }
}

# Grant necessary privileges for service control
try {
    $tempFile = [System.IO.Path]::GetTempFileName()
    $content = @"
[Unicode]
Unicode=yes
[Version]
signature="$CHICAGO$"
Revision=1
[Privilege Rights]
SeServiceLogonRight = *S-1-5-19
SeManageVolumePrivilege = *S-1-5-19
"@
    Set-Content -Path $tempFile -Value $content
    
    $result = & secedit /configure /db secedit.sdb /cfg $tempFile /areas USER_RIGHTS 2>&1
    Remove-Item $tempFile -Force
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Successfully granted service control privileges to LOCAL SERVICE"
    } else {
        Write-Warning "Failed to grant service control privileges: $result"
    }
} catch {
    Write-Warning "Could not grant service control privileges: $_"
}

Write-Host "Permission granting process completed"
