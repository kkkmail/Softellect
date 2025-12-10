# Function to update the reinstallation state file
function Update-ReinstallStateFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,
        
        [Parameter(Mandatory = $true)]
        [int]$ErrorCode,
        
        [Parameter(Mandatory = $false)]
        [string]$ErrorMessage = "",
        
        [Parameter(Mandatory = $false)]
        [string]$AdditionalInfo = ""
    )
    
    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-StateFilePath.ps1"
    . "$scriptDirectory\Get-ErrorDescription.ps1"
    
    try {
        # Get state file path and ensure directory exists
        $stateFilePath = Get-StateFilePath -ServiceFolder $ServiceFolder -EnsureDirectoryExists $true
        if (-not $stateFilePath) {
            Write-ServiceLog -Level Error -Message "Failed to get state file path"
            return $false
        }
        
        # Extract service name for the state object
        $serviceName = Split-Path -Path $ServiceFolder -Leaf
        $fullServiceName = Split-Path -Path $stateFilePath -Leaf
        $fullServiceName = [System.IO.Path]::GetFileNameWithoutExtension($fullServiceName)
        
        # Create state object
        $stateObject = @{
            ServiceName = $serviceName
            FullServiceName = $fullServiceName
            ErrorCode = $ErrorCode
            ErrorDescription = Get-ErrorDescription -ErrorCode $ErrorCode
            ErrorMessage = $ErrorMessage
            AdditionalInfo = $AdditionalInfo
            Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
            ServiceFolder = $ServiceFolder
        }
        
        # Convert to JSON
        $jsonContent = $stateObject | ConvertTo-Json -Depth 3
        
        # Write atomically using temporary file
        $tempFilePath = "$stateFilePath.tmp"
        
        # Write to temporary file first
        $jsonContent | Out-File -FilePath $tempFilePath -Encoding UTF8 -Force
        
        # Perform atomic replacement
        if (Test-Path -Path $stateFilePath) {
            # Replace the original file with the temp file atomically
            # PowerShell doesn't have File.Replace, so we use .NET directly
            [System.IO.File]::Replace($tempFilePath, $stateFilePath, $null)
        }
        else {
            # Move the file (atomic on the same volume)
            Move-Item -Path $tempFilePath -Destination $stateFilePath -Force
        }
        
        Write-ServiceLog -Message "State file updated: $stateFilePath (ErrorCode: $ErrorCode)"
        
        return $true
    }
    catch {
        Write-ServiceLog -Level Error -Message "Failed to update state file: $_"
        
        # Clean up temporary file if it exists
        $tempFilePath = "$stateFilePath.tmp"
        if (Test-Path -Path $tempFilePath) {
            try {
                Remove-Item -Path $tempFilePath -Force -ErrorAction SilentlyContinue
            }
            catch {
                Write-ServiceLog -Level Warning -Message "Failed to clean up temporary file: $tempFilePath"
            }
        }
        
        return $false
    }
}
