function Copy-FilesToKeep {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder,
        
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,
        
        [Parameter(Mandatory = $false)]
        [string[]]$FilesToKeep = @("appsettings.json")
    )

    $errors = @()

    foreach ($file in $FilesToKeep) {
        try {
            $backupFile = Join-Path -Path $BackupFolder -ChildPath $file

            # Check if file exists in backup
            if (Test-Path -Path $backupFile) {
                $destFile = Join-Path -Path $ServiceFolder -ChildPath $file

                # Create directory structure if needed
                $destDir = Split-Path -Path $destFile -Parent
                if (-not (Test-Path -Path $destDir)) {
                    Write-ServiceLog -Message "Creating directory: $destDir"
                    New-Item -Path $destDir -ItemType Directory -Force | Out-Null
                }

                # Copy file to service folder
                Copy-Item -Path $backupFile -Destination $destFile -Force -ErrorAction Stop
                Write-ServiceLog -Message "Kept file: $file"
            }
            else {
                Write-ServiceLog -Level Warning -Message "File to keep not found in backup: $file"
            }
        }
        catch {
            $errors += "Failed to copy file '$file' to keep: $_"
        }
    }

    # Verify that all files to keep were copied
    foreach ($file in $FilesToKeep) {
        $destFile = Join-Path -Path $ServiceFolder -ChildPath $file
        if (-not (Test-Path -Path $destFile)) {
            $errors += "File to keep '$file' was not found in the service folder after copying."
        }
    }

    return $errors
}
