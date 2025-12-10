function Start-WindowsService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load Write-ServiceLog
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"
    . "$scriptDirectory\Get-ServiceName.ps1"

    Write-ServiceLog -Message "Attempting to start service: $ServiceName..."
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($service) {
        if ($service.Status -eq 'Running') {
            Write-ServiceLog -Message "Service: $ServiceName is already running."
            return
        }
    }

    try {
        # Trying to start new service.
        # Set-PSDebug -Trace 2
        Write-ServiceLog -Message "Trying to start new service: $ServiceName."
        Start-Service -Name $ServiceName -ErrorAction Stop

        # Check that service has started.
        Write-ServiceLog -Message "Waiting 5 seconds to give service time to start..."
        # Set-PSDebug -Off
        Start-Sleep -Seconds 5
        $testService = Get-Service -Name $ServiceName

        if ($testService.Status -ne "Running") {
            [string] $errMessage = "Failed to start service: $ServiceName"
            Write-ServiceLog -Message $errMessage -Level "Error"
            throw $errMessage
        }
        else {
            Write-ServiceLog -Message "Started service: $ServiceName."
        }
    }
    catch {
        Write-ServiceLog -Message "Error starting service $($ServiceName): $_" -Level "Error"
        throw $_
    }
}
