$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\VpnClientFunctions.ps1"

UninstallVpnClient
InstallVpnClient
