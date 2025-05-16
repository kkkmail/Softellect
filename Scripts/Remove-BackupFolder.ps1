# Function to clean up temporary backup folder
function Remove-BackupFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder
    )

    try {
        Write-ServiceLog -Message "Cleaning up temporary backup folder: $BackupFolder"

        if (Test-Path -Path $BackupFolder) {
            # Try to delete the backup folder
            Remove-Item -Path $BackupFolder -Recurse -Force -ErrorAction SilentlyContinue

            # Check if deletion was successful
            if (Test-Path -Path $BackupFolder) {
                Write-ServiceLog -Level Warning -Message "Could not completely remove backup folder. Some temporary files may remain in: $BackupFolder"
            }
            else {
                Write-ServiceLog -Message "Backup folder cleaned up successfully."
            }
        }
        else {
            Write-ServiceLog -Level Warning -Message "Backup folder does not exist: $BackupFolder"
        }

        # Return success regardless of whether cleanup succeeded
        return $true
    }
    catch {
        Write-ServiceLog -Level Warning -Message "Error cleaning up backup folder: $_"
        # Still return success as we don't want to fail the script for cleanup issues
        return $true
    }
}
