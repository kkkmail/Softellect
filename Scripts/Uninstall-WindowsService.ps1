function Uninstall-WindowsService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"
    . "$scriptDirectory\Uninstall-ServiceWithScExe.ps1"
    . "$scriptDirectory\Uninstall-ServiceWithWMI.ps1"
    . "$scriptDirectory\Uninstall-ServiceWithDotNet.ps1"

    Write-ServiceLog -Message "Attempting to stop service: $ServiceName..."
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($service) {
        if ($service.Status -eq 'Running') {
            Write-ServiceLog -Message "Stopping service: $ServiceName."
            try {
                Stop-Service -Name $ServiceName -Force -ErrorAction Stop
                Write-ServiceLog -Message "Stopped service: $ServiceName."
            }
            catch {
                Write-ServiceLog -Message "Error stopping service: $_" -Level "Error"
                # Continue with uninstall even if stop fails
            }
        }
        else {
            Write-ServiceLog -Message "Service: $ServiceName is not running."
        }

        Write-ServiceLog -Message "Attempting to uninstall service: $ServiceName..."

        # Check if Remove-Service cmdlet is available (PS 6+)
        if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
            try {
                Remove-Service -Name $ServiceName -ErrorAction Stop
                Write-ServiceLog -Message "Uninstalled service: $ServiceName via Remove-Service cmdlet."
                return
            }
            catch {
                Write-ServiceLog -Message "Remove-Service cmdlet failed: $_. Trying alternative methods..." -Level "Warning"
            }
        }

        # Try sc.exe method
        if (Uninstall-ServiceWithScExe -ServiceName $ServiceName) {
            return
        }

        # Try WMI method
        if (Uninstall-ServiceWithWMI -ServiceName $ServiceName) {
            return
        }

        # Try .NET ServiceInstaller method
        if (Uninstall-ServiceWithDotNet -ServiceName $ServiceName) {
            return
        }

        # If all methods fail, throw an error
        throw "Failed to uninstall service: $ServiceName. All methods failed."
    }
    else {
        Write-ServiceLog -Message "Service: $ServiceName is not found."
    }
}
