# Function to stop and uninstall service
function Uninstall-Service {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,

        [Parameter(Mandatory = $true)]
        [string]$UninstallScriptName
    )

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to service folder
        Set-Location -Path $ServiceFolder

        # Run uninstall script
        Write-ServiceLog -Message "Running uninstall script: $UninstallScriptName"
        $uninstallScript = Join-Path -Path $ServiceFolder -ChildPath $UninstallScriptName

        # Execute uninstall script and capture its output
        $hadError = $false

        try {
            $output = & $uninstallScript 2>&1

            # If the script ran without throwing, check its output for error messages
            if ($output -match "error|fail|exception" -and $output -notmatch "uninstalled service") {
                $hadError = $true
                Write-ServiceLog -Level Error -Message "Uninstall script reported errors in its output."
            }
        }
        catch {
            # This captures errors thrown by the script
            Write-ServiceLog -Level Error -Message "Uninstall script threw an exception: $_"
            $hadError = $true
        }

        # Verify service is uninstalled by checking if it still exists
        $serviceName = $null

        # Try to extract service name from the script content
        try {
            $scriptContent = Get-Content -Path $uninstallScript -Raw
            if ($scriptContent -match "service:\s+([a-zA-Z0-9_\-]+)") {
                $serviceName = $matches[1]
            }
        }
        catch {
            Write-ServiceLog -Level Warning -Message "Could not read uninstall script content to extract service name."
        }

        # If we can't extract the service name from the script, look for clues in the output
        if (-not $serviceName -and $output -match "service:\s+([a-zA-Z0-9_\-]+)") {
            $serviceName = $matches[1]
        }

        if ($serviceName -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            Write-ServiceLog -Level Error -Message "Service '$serviceName' is still present after uninstall script execution."
            $hadError = $true
        }

        # Restore original location
        Set-Location -Path $currentLocation

        if ($hadError) {
            Write-ServiceLog -Level Error -Message "Uninstall output: $output"
            return $false
        }

        Write-ServiceLog -Message "Service uninstalled successfully."
        return $true
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation
        Write-ServiceLog -Level Error -Message "Failed to uninstall service: $_"
        return $false
    }
}
