function Start-DistributedService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ServiceName.ps1"
    . "$scriptDirectory\Start-WindowsService.ps1"

    [string] $windowsServiceName = Get-ServiceName -ServiceName $ServiceName
    Start-WindowsService -ServiceName $windowsServiceName
}
