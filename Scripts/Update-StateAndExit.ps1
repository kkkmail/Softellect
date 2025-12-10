# Function to update state file and exit the script
function Update-StateAndExit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,

        [Parameter(Mandatory = $true)]
        [int]$ErrorCode,

        [Parameter(Mandatory = $false)]
        [string]$ErrorMessage = "",

        [Parameter(Mandatory = $false)]
        [string]$AdditionalInfo = ""
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Update-ReinstallStateFile.ps1"

    # Update the state file
    $updateResult = Update-ReinstallStateFile -ServiceFolder $ServiceFolder -ErrorCode $ErrorCode -ErrorMessage $ErrorMessage -AdditionalInfo $AdditionalInfo

    # Exit with the error code
    Exit $ErrorCode
}
