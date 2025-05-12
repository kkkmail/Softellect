# Get the directory of this script
$scriptDirectory = $PSScriptRoot

# Load individual function files using absolute paths
. "$scriptDirectory\PartitionerVersionInfo.ps1"
. "$scriptDirectory\PartitionerServiceName.ps1"

. "$scriptDirectory\Install-DistributedService.ps1"
. "$scriptDirectory\Uninstall-DistributedService.ps1"
. "$scriptDirectory\Start-DistributedService.ps1"
. "$scriptDirectory\Stop-DistributedService.ps1"
. "$scriptDirectory\Write-ServiceLog.ps1"

$MessagingDataVersion = $global:messagingDataVersion
$VersionNumber = $global:messagingDataVersion
$ServiceName = $global:partitionerServiceName

function InstallPartitionerService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string]$Login = "NT AUTHORITY\LOCAL SERVICE",

        [Parameter(Mandatory = $false)]
        [string]$Password = ""
    )

    # Log function parameters
    Write-ServiceLog -Message "InstallPartitionerService - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"
    Write-ServiceLog -Message "  MessagingDataVersion = '$MessagingDataVersion'" -Level "Info"
    Write-ServiceLog -Message "  VersionNumber = '$VersionNumber'" -Level "Info"

    Install-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion -VersionNumber $VersionNumber -Login $Login -Password $Password
}


function UninstallPartitionerService {
    [CmdletBinding()]
    param ()

    Uninstall-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
}

function StartPartitionerService {
    [CmdletBinding()]
    param ()

    Start-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
}

function StopPartitionerService {
    [CmdletBinding()]
    param ()

    Stop-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
}
