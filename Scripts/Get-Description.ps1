function Get-Description {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $false)]
        [string]$MessagingDataVersion = "",

        [Parameter(Mandatory = $false)]
        [string]$VersionNumber = ""
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ValueOrDefault.ps1"

    $MessagingDataVersion = Get-ValueOrDefault -Value $MessagingDataVersion -DefaultValue $global:messagingDataVersion
    [string]$description = "$ServiceName, version $VersionNumber.$MessagingDataVersion"
    return $description
}
