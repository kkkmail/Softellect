# We need workerNodeServiceName but messagingDataVersion here.

# Get the directory of this script
$scriptDirectory = $PSScriptRoot

# Load individual function files using absolute paths
. "$scriptDirectory\WorkerNodeVersionInfo.ps1"
. "$scriptDirectory\WorkerNodeServiceName.ps1"
. "$scriptDirectory\Install-DistributedService.ps1"
. "$scriptDirectory\Uninstall-DistributedService.ps1"
. "$scriptDirectory\Start-DistributedService.ps1"
. "$scriptDirectory\Stop-DistributedService.ps1"
. "$scriptDirectory\Write-ServiceLog.ps1"

$MessagingDataVersion = $global:messagingDataVersion
$VersionNumber = $global:messagingDataVersion
$ServiceName = $global:workerNodeServiceName

function InstallWorkerNodeService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string]$Login = "NT AUTHORITY\LOCAL SERVICE",

        [Parameter(Mandatory = $false)]
        [string]$Password = ""
    )

    # Log function parameters
    Write-ServiceLog -Message "InstallWorkerNodeService - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"
    Write-ServiceLog -Message "  MessagingDataVersion = '$MessagingDataVersion'" -Level "Info"
    Write-ServiceLog -Message "  VersionNumber = '$VersionNumber'" -Level "Info"

    Install-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion -VersionNumber $VersionNumber -Login $Login -Password $Password
}

function UninstallWorkerNodeService {
    [CmdletBinding()]
    param ()

    Uninstall-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
}

function StartWorkerNodeService {
    [CmdletBinding()]
    param ()

    Start-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
}

function StopWorkerNodeService {
    [CmdletBinding()]
    param ()

    Stop-DistributedService -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
}
