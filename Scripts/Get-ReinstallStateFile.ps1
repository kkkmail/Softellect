# Function to read the reinstallation state file
function Get-ReinstallStateFile {
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
            return $null
        }
        
        if (-not (Test-Path -Path $stateFilePath)) {
            Write-ServiceLog -Level Warning -Message "State file not found: $stateFilePath"
            return $null
        }
        
        # Read and parse JSON
        $jsonContent = Get-Content -Path $stateFilePath -Raw -Encoding UTF8
        $stateObject = $jsonContent | ConvertFrom-Json
        
        return $stateObject
    }
    catch {
        Write-ServiceLog -Level Error -Message "Failed to read state file: $_"
        return $null
    }
}