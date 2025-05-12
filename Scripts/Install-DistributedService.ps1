function Install-DistributedService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$MessagingDataVersion,

        [Parameter(Mandatory = $true)]
        [string]$VersionNumber,

        [Parameter(Mandatory = $false)]
        [string]$Login = "NT AUTHORITY\LOCAL SERVICE",

        [Parameter(Mandatory = $false)]
        [string]$Password = ""
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ServiceName.ps1"
    . "$scriptDirectory\Get-BinaryPathName.ps1"
    . "$scriptDirectory\Get-Description.ps1"
    . "$scriptDirectory\Reinstall-WindowsService.ps1"
    . "$scriptDirectory\Write-ServiceLog.ps1"

    # Log function parameters
    Write-ServiceLog -Message "Install-DistributedService - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"
    Write-ServiceLog -Message "  MessagingDataVersion = '$MessagingDataVersion'" -Level "Info"
    Write-ServiceLog -Message "  VersionNumber = '$VersionNumber'" -Level "Info"
    Write-ServiceLog -Message "  Login = '$Login'" -Level "Info"

    [string] $windowsServiceName = Get-ServiceName -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
    [string] $binaryPath = Get-BinaryPathName -ServiceName $ServiceName
    [string] $description = Get-Description -ServiceName $ServiceName -VersionNumber $VersionNumber -MessagingDataVersion $MessagingDataVersion
    Write-ServiceLog -Message "  windowsServiceName = '$windowsServiceName'" -Level "Info"
    Write-ServiceLog -Message "  binaryPath = '$binaryPath'" -Level "Info"
    Write-ServiceLog -Message "  description = '$description'" -Level "Info"

    Reinstall-WindowsService -ServiceName $windowsServiceName -BinaryPath $binaryPath -Description $description -Login $Login -Password $Password
}
