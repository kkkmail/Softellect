# Get the directory of this script
$scriptDirectory = $PSScriptRoot

# Load individual function files using absolute paths
. "$scriptDirectory\MessagingServiceName.ps1"

. "$scriptDirectory\Install-DistributedService.ps1"
. "$scriptDirectory\Uninstall-DistributedService.ps1"
. "$scriptDirectory\Start-DistributedService.ps1"
. "$scriptDirectory\Stop-DistributedService.ps1"
. "$scriptDirectory\Write-ServiceLog.ps1"

$MessagingDataVersion = $global:messagingDataVersion
$VersionNumber = $global:messagingDataVersion
$ServiceName = $global:messagingServiceName

function InstallMessagingService {
    [CmdletBinding()]
    param ()

    # Log function parameters
    Write-ServiceLog -Message "InstallMessagingService - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"

    Install-DistributedService -ServiceName $ServiceName
}

function UninstallMessagingService {
    [CmdletBinding()]
    param ()

    Uninstall-DistributedService -ServiceName $ServiceName
}

function StartMessagingService {
    [CmdletBinding()]
    param ()

    Start-DistributedService -ServiceName $ServiceName
}

function StopMessagingService {
    [CmdletBinding()]
    param ()

    Stop-DistributedService -ServiceName $ServiceName
}
