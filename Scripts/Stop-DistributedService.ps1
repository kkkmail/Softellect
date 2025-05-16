function Stop-DistributedService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ServiceName.ps1"
    . "$scriptDirectory\Stop-WindowsService.ps1"

    [string] $windowsServiceName = Get-ServiceName -ServiceName $ServiceName
    Stop-WindowsService -ServiceName $windowsServiceName
}
