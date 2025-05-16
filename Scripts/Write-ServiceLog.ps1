function Write-ServiceLog {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $false)]
        [ValidateSet("Info", "Warning", "Error")]
        [string]$Level = "Info"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $formattedMessage = "[$timestamp] [$Level] $Message"

    switch ($Level) {
        "Info" { Write-Host $formattedMessage -ForegroundColor White }
        "Warning" { Write-Host $formattedMessage -ForegroundColor Yellow }
        "Error" { Write-Host $formattedMessage -ForegroundColor Red }
    }
}
