# Function to install service
function Install-Service {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,

        [Parameter(Mandatory = $false)]
        [string]$InstallScriptName = "Install-WorkerNodeService.ps1"
    )

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to service folder
        Set-Location -Path $ServiceFolder

        # Run install script
        Write-ServiceLog -Message "Running install script: $InstallScriptName"
        $installScript = Join-Path -Path $ServiceFolder -ChildPath $InstallScriptName

        # Execute install script and capture its output
        $hadError = $false

        try {
            $output = & $installScript 2>&1

            # If the script ran without throwing, check its output for error messages
            if ($output -match "error|fail|exception" -and $output -notmatch "started service") {
                $hadError = $true
                Write-ServiceLog -Level Error -Message "Install script reported errors in its output."
            }
        }
        catch {
            # This captures errors thrown by the script
            Write-ServiceLog -Level Error -Message "Install script threw an exception: $_"
            $hadError = $true
        }

        # Verify service is installed by checking if it exists and is running
        $serviceName = $null

        # Try to extract service name from the script content
        try {
            $scriptContent = Get-Content -Path $installScript -Raw
            if ($scriptContent -match "service:\s+([a-zA-Z0-9_\-]+)") {
                $serviceName = $matches[1]
            }
        }
        catch {
            Write-ServiceLog -Level Warning -Message "Could not read install script content to extract service name."
        }

        # If we can't extract the service name from the script, look for clues in the output
        if (-not $serviceName -and $output -match "service:\s+([a-zA-Z0-9_\-]+)") {
            $serviceName = $matches[1]
        }

        if ($serviceName) {
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if (-not $service) {
                Write-ServiceLog -Level Error -Message "Service '$serviceName' is not present after install script execution."
                $hadError = $true
            }
            elseif ($service.Status -ne 'Running') {
                Write-ServiceLog -Level Error -Message "Service '$serviceName' is not running after install script execution."
                $hadError = $true
            }
        }

        # Restore original location
        Set-Location -Path $currentLocation

        if ($hadError) {
            if ($output) {
                Write-ServiceLog -Level Error -Message "Install output: $output"
            }
            return $false
        }

        Write-ServiceLog -Message "Service installed successfully."
        return $true
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation
        Write-ServiceLog -Level Error -Message "Failed to install service: $_"
        return $false
    }
}
