function Install-DistributedService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $false)]
        [string]$MessagingDataVersion = "",

        [Parameter(Mandatory = $false)]
        [string]$VersionNumber = "",

        [Parameter(Mandatory = $false)]
        [string]$Login = "NT AUTHORITY\LOCAL SERVICE",

        [Parameter(Mandatory = $false)]
        [string]$Password = ""
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ValueOrDefault.ps1"
    . "$scriptDirectory\Get-ServiceName.ps1"
    . "$scriptDirectory\Get-BinaryPathName.ps1"
    . "$scriptDirectory\Get-Description.ps1"
    . "$scriptDirectory\Reinstall-WindowsService.ps1"

    $VersionNumber = Get-ValueOrDefault -Value $VersionNumber -DefaultValue $global:versionNumber
    [string] $windowsServiceName = Get-ServiceName -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
    [string] $binaryPath = Get-BinaryPathName -ServiceName $ServiceName
    [string] $description = Get-Description -ServiceName $ServiceName -VersionNumber $VersionNumber -MessagingDataVersion $MessagingDataVersion
    Reinstall-WindowsService -ServiceName $windowsServiceName -BinaryPath $binaryPath -Description $description -Login $Login -Password $Password
}
