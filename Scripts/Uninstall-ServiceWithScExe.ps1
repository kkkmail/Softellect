function Uninstall-ServiceWithScExe {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load Write-ServiceLog
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"

    try {
        $output = & sc.exe delete $ServiceName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-ServiceLog -Message "Uninstalled service: $ServiceName via sc.exe."
            return $true
        }
        else {
            Write-ServiceLog -Message "sc.exe delete failed with exit code: $LASTEXITCODE. Output: $output" -Level "Warning"
            return $false
        }
    }
    catch {
        Write-ServiceLog -Message "sc.exe delete failed with exception: $_" -Level "Error"
        return $false
    }
}
