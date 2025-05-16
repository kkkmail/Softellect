function Uninstall-ServiceWithWMI {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load Write-ServiceLog
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"

    try {
        $wmiService = Get-WmiObject -Class Win32_Service -Filter "Name='$ServiceName'"
        if ($wmiService) {
            $result = $wmiService.Delete()
            if ($result.ReturnValue -eq 0) {
                Write-ServiceLog -Message "Uninstalled service: $ServiceName via WMI."
                return $true
            }
            else {
                Write-ServiceLog -Message "WMI delete failed with return value: $($result.ReturnValue)." -Level "Warning"
                return $false
            }
        }
        else {
            Write-ServiceLog -Message "WMI service not found." -Level "Warning"
            return $false
        }
    }
    catch {
        Write-ServiceLog -Message "WMI delete failed with exception: $_" -Level "Error"
        return $false
    }
}
