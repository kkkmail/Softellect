function Stop-DistributedService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $false)]
        [string]$MessagingDataVersion = ""
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ServiceName.ps1"
    . "$scriptDirectory\Stop-WindowsService.ps1"

    [string] $windowsServiceName = Get-ServiceName -ServiceName $ServiceName -MessagingDataVersion $MessagingDataVersion
    Stop-WindowsService -ServiceName $windowsServiceName
}
