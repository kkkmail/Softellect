function Get-Description {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\BuildInfo.ps1"
    . "$scriptDirectory\MessagingVersionInfo.ps1"

    $MessagingDataVersion = $global:messagingDataVersion
    $VersionNumber = $global:buildNumber

    [string]$description = "$ServiceName, version: $VersionNumber, messaging data version: $MessagingDataVersion."
    return $description
}
