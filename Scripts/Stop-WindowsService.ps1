# https://stackoverflow.com/questions/35064964/powershell-script-to-check-if-service-is-started-if-not-then-start-it
function Stop-WindowsService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load Write-ServiceLog
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"

    Write-ServiceLog -Message "Attempting to stop service: $ServiceName..."

    try {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

        if ($service) {
            if ($service.Status -ne 'Running') {
                Write-ServiceLog -Message "Service: $ServiceName is not running."
            }
            else {
                Stop-Service -Name $ServiceName -ErrorAction Stop
                Write-ServiceLog -Message "Stopped service: $ServiceName."
            }
        }
        else {
            Write-ServiceLog -Message "Service: $ServiceName is not found."
        }
    }
    catch {
        Write-ServiceLog -Message "Error stopping service $($ServiceName): $_" -Level "Error"
    }
}
