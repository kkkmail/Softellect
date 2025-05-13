# Get the directory of this script
$scriptDirectory = $PSScriptRoot

# Load individual function files using absolute paths
. "$scriptDirectory\PartitionerServiceName.ps1"

. "$scriptDirectory\Install-DistributedService.ps1"
. "$scriptDirectory\Uninstall-DistributedService.ps1"
. "$scriptDirectory\Start-DistributedService.ps1"
. "$scriptDirectory\Stop-DistributedService.ps1"
. "$scriptDirectory\Write-ServiceLog.ps1"

$ServiceName = $global:partitionerServiceName

function InstallPartitionerService {
    [CmdletBinding()]
    param ()

    # Log function parameters
    Write-ServiceLog -Message "InstallPartitionerService - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"

    Install-DistributedService -ServiceName $ServiceName
}

function UninstallPartitionerService {
    [CmdletBinding()]
    param ()

    Uninstall-DistributedService -ServiceName $ServiceName
}

function StartPartitionerService {
    [CmdletBinding()]
    param ()

    Start-DistributedService -ServiceName $ServiceName
}

function StopPartitionerService {
    [CmdletBinding()]
    param ()

    Stop-DistributedService -ServiceName $ServiceName
}
