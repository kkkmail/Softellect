# Restore files from backup
function Restore-ServiceFromBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder,

        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder
    )

    $errors = @()

    try {
        # Clear service folder first
        $clearErrors = Clear-ServiceFolder -ServiceFolder $ServiceFolder
        if ($clearErrors) {
            $errors += $clearErrors
            Write-ServiceLog -Level Warning -Message "Could not fully clear service folder before restore."
        }

        # Check if backup folder exists
        if (-not (Test-Path -Path $BackupFolder)) {
            $errors += "Backup folder '$BackupFolder' does not exist."
            return $errors
        }

        # Copy all files from backup to service folder
        Write-ServiceLog -Message "Copying files from backup folder to service folder..."
        try {
            # Try bulk copy first
            Copy-Item -Path "$BackupFolder\*" -Destination $ServiceFolder -Recurse -Force -ErrorAction Stop
            Write-ServiceLog -Message "All files restored from backup successfully."
        }
        catch {
            Write-ServiceLog -Level Warning -Message "Bulk restore failed, falling back to individual file restore: $_"

            # If bulk copy fails, copy files individually
            $files = Get-ChildItem -Path $BackupFolder -Recurse

            foreach ($file in $files) {
                try {
                    # Get the relative path from backup folder
                    $relativePath = $file.FullName.Substring($BackupFolder.Length)
                    $destinationPath = Join-Path -Path $ServiceFolder -ChildPath $relativePath

                    # If it's a directory, create it
                    if ($file.PSIsContainer) {
                        if (-not (Test-Path -Path $destinationPath)) {
                            New-Item -Path $destinationPath -ItemType Directory -Force | Out-Null
                        }
                        Write-ServiceLog -Message "Created directory during restore: $destinationPath"
                    }
                    # If it's a file, copy it
                    else {
                        # Ensure the destination directory exists
                        $destinationDir = Split-Path -Path $destinationPath -Parent
                        if (-not (Test-Path -Path $destinationDir)) {
                            New-Item -Path $destinationDir -ItemType Directory -Force | Out-Null
                            Write-ServiceLog -Message "Created directory during restore: $destinationDir"
                        }

                        # Copy the file
                        Copy-Item -Path $file.FullName -Destination $destinationPath -Force
                        Write-ServiceLog -Message "Restored file: $($file.FullName) to $destinationPath"
                    }
                }
                catch {
                    $errors += "Failed to restore '$($file.FullName)': $_"
                }
            }
        }

        # Verify all files were restored correctly
        $backupItems = Get-ChildItem -Path $BackupFolder -Recurse | Where-Object { -not $_.PSIsContainer }
        $restoredItems = Get-ChildItem -Path $ServiceFolder -Recurse | Where-Object { -not $_.PSIsContainer }

        if ($backupItems.Count -gt $restoredItems.Count) {
            $errors += "Not all files were restored. Backup count: $($backupItems.Count), Restored count: $($restoredItems.Count)"
        }
    }
    catch {
        $errors += "Failed to restore service from backup: $_"
    }

    return $errors
}
