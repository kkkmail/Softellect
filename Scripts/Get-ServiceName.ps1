function Get-ServiceName {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $false)]
        [string]$MessagingDataVersion = ""
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ValueOrDefault.ps1"

    $MessagingDataVersion = Get-ValueOrDefault -Value $MessagingDataVersion -DefaultValue $global:messagingDataVersion
    return "$ServiceName-$MessagingDataVersion"
}
