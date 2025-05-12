function Get-ValueOrDefault {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string]$Value = "",

        [Parameter(Mandatory = $false)]
        [string]$MessagingDataVersion = "",

        [Parameter(Mandatory = $true)]
        [string]$DefaultValue
    )

    if ($Value -eq "") {
        $Value = $DefaultValue
    }

    return $Value
}
