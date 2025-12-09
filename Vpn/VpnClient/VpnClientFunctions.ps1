# Get the directory of this script
$scriptDirectory = $PSScriptRoot

# Load individual function files using absolute paths
. "$scriptDirectory\VpnClientName.ps1"

# These scripts are expected to be copied from the Softellect Scripts folder
. "$scriptDirectory\Install-DistributedService.ps1"
. "$scriptDirectory\Uninstall-DistributedService.ps1"
. "$scriptDirectory\Start-DistributedService.ps1"
. "$scriptDirectory\Stop-DistributedService.ps1"
. "$scriptDirectory\Write-ServiceLog.ps1"
. "$scriptDirectory\Grant-WfpPermissions.ps1"

$ServiceName = $global:vpnClientServiceName

function InstallVpnClient {
    [CmdletBinding()]
    param ()

    Write-ServiceLog -Message "InstallVpnClient - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"

    # Grant WFP permissions to LOCAL SERVICE before installing the service
    # This is required for the kill-switch functionality
    Write-ServiceLog -Message "Granting WFP permissions to LOCAL SERVICE..." -Level "Info"
    Grant-WfpPermissions

    Install-DistributedService -ServiceName $ServiceName
}

function UninstallVpnClient {
    [CmdletBinding()]
    param ()

    Uninstall-DistributedService -ServiceName $ServiceName
}

function StartVpnClient {
    [CmdletBinding()]
    param ()

    Start-DistributedService -ServiceName $ServiceName
}

function StopVpnClient {
    [CmdletBinding()]
    param ()

    Stop-DistributedService -ServiceName $ServiceName
}
