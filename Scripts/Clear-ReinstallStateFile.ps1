# Function to clear the reinstallation state file
function Clear-ReinstallStateFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder
    )
    
    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-StateFilePath.ps1"
    
    try {
        # Get state file path (don't create directory if it doesn't exist)
        $stateFilePath = Get-StateFilePath -ServiceFolder $ServiceFolder -EnsureDirectoryExists $false
        if (-not $stateFilePath) {
            Write-ServiceLog -Level Error -Message "Failed to get state file path"
            return $false
        }
        
        if (Test-Path -Path $stateFilePath) {
            Remove-Item -Path $stateFilePath -Force
            Write-ServiceLog -Message "State file cleared: $stateFilePath"
        }
        
        return $true
    }
    catch {
        Write-ServiceLog -Level Error -Message "Failed to clear state file: $_"
        return $false
    }
}
