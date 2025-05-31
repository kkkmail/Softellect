# Function to get error description for service reinstallation error codes
function Get-ErrorDescription {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$ErrorCode
    )
    
    # Get the script directory and load error codes
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\ServiceReinstallErrorCodes.ps1"
    
    if ($global:ERROR_DESCRIPTIONS.ContainsKey($ErrorCode)) {
        return $global:ERROR_DESCRIPTIONS[$ErrorCode]
    }
    return "Unknown error code: $ErrorCode"
}
