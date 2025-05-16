function Get-ServiceName {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\MessagingVersionInfo.ps1"

    $MessagingDataVersion = $global:messagingDataVersion
    return "$ServiceName-$MessagingDataVersion"
}
