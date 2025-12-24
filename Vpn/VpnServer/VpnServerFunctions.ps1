# Get the directory of this script
$scriptDirectory = $PSScriptRoot

# Load individual function files using absolute paths
. "$scriptDirectory\VpnServerName.ps1"

# These scripts are expected to be copied from the Softellect Scripts folder
. "$scriptDirectory\Install-DistributedService.ps1"
. "$scriptDirectory\Uninstall-DistributedService.ps1"
. "$scriptDirectory\Start-DistributedService.ps1"
. "$scriptDirectory\Stop-DistributedService.ps1"
. "$scriptDirectory\Write-ServiceLog.ps1"

$ServiceName = $global:vpnServerServiceName

function InstallVpnServer {
    [CmdletBinding()]
    param ()

    Write-ServiceLog -Message "InstallVpnServer - Parameters:" -Level "Info"
    Write-ServiceLog -Message "  scriptDirectory = '$scriptDirectory'" -Level "Info"
    Write-ServiceLog -Message "  ServiceName = '$ServiceName'" -Level "Info"

    Install-DistributedService -ServiceName $ServiceName -Login "NT AUTHORITY\SYSTEM"
}

function UninstallVpnServer {
    [CmdletBinding()]
    param ()

    Uninstall-DistributedService -ServiceName $ServiceName
}

function StartVpnServer {
    [CmdletBinding()]
    param ()

    Start-DistributedService -ServiceName $ServiceName
}

function StopVpnServer {
    [CmdletBinding()]
    param ()

    Stop-DistributedService -ServiceName $ServiceName
}
