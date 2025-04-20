#Requires -RunAsAdministrator
param (
    [Parameter(Mandatory = $true)]
    [string]$FolderPath
)

# This script grants the LOCAL SERVICE account the necessary permissions
# to manage services and write to the specified folder

$localServiceSid = New-Object System.Security.Principal.SecurityIdentifier "NT AUTHORITY\LOCAL SERVICE"
$localServiceAccount = $localServiceSid.Translate([System.Security.Principal.NTAccount])

# Grant service management permissions
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

# Grant folder access permissions
if (Test-Path -Path $FolderPath) {
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
} else {
    Write-Error "Folder path does not exist: $FolderPath"
}

# Add LOCAL SERVICE to Performance Log Users group for service control
Add-LocalGroupMember -Group "Performance Log Users" -Member "NT AUTHORITY\LOCAL SERVICE" -ErrorAction SilentlyContinue

Write-Host "Successfully granted LOCAL SERVICE account necessary permissions"
