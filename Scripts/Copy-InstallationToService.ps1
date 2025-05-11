function Copy-InstallationToService {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,
        
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder
    )

    $errors = @()

    try {
        # First check if the installation folder exists and has content
        if (-not (Test-Path -Path $InstallationFolder)) {
            $errors += "Installation folder '$InstallationFolder' does not exist."
            return $errors
        }

        # Get items in the installation folder
        $items = Get-ChildItem -Path $InstallationFolder -Force
        if (-not $items) {
            $errors += "Installation folder '$InstallationFolder' is empty."
            return $errors
        }

        # Create the service folder if it doesn't exist
        if (-not (Test-Path -Path $ServiceFolder)) {
            New-Item -Path $ServiceFolder -ItemType Directory -Force | Out-Null
        }

        # Copy all files from installation folder to service folder
        Write-ServiceLog -Message "Copying files from '$InstallationFolder' to '$ServiceFolder'..."
        try {
            # Try bulk copy first
            Copy-Item -Path "$InstallationFolder\*" -Destination $ServiceFolder -Recurse -Force -ErrorAction Stop
            Write-ServiceLog -Message "All files copied successfully."
        }
        catch {
            Write-ServiceLog -Level Warning -Message "Bulk copy failed, falling back to individual file copy: $_"

            # If bulk copy fails, copy files individually
            $files = Get-ChildItem -Path $InstallationFolder -Recurse

            foreach ($file in $files) {
                try {
                    # Get the relative path from installation folder
                    $relativePath = $file.FullName.Substring($InstallationFolder.Length)
                    $destinationPath = Join-Path -Path $ServiceFolder -ChildPath $relativePath

                    # If it's a directory, create it
                    if ($file.PSIsContainer) {
                        if (-not (Test-Path -Path $destinationPath)) {
                            New-Item -Path $destinationPath -ItemType Directory -Force | Out-Null
                        }
                        Write-ServiceLog -Message "Created directory: $destinationPath"
                    }
                    # If it's a file, copy it
                    else {
                        # Ensure the destination directory exists
                        $destinationDir = Split-Path -Path $destinationPath -Parent
                        if (-not (Test-Path -Path $destinationDir)) {
                            New-Item -Path $destinationDir -ItemType Directory -Force | Out-Null
                            Write-ServiceLog -Message "Created directory: $destinationDir"
                        }

                        # Copy the file
                        Copy-Item -Path $file.FullName -Destination $destinationPath -Force
                        Write-ServiceLog -Message "Copied file: $($file.FullName) to $destinationPath"
                    }
                }
                catch {
                    $errors += "Failed to copy '$($file.FullName)': $_"
                }
            }
        }

        # Verify all files were copied correctly
        $sourceItems = Get-ChildItem -Path $InstallationFolder -Recurse | Where-Object { -not $_.PSIsContainer }
        $destItems = Get-ChildItem -Path $ServiceFolder -Recurse | Where-Object { -not $_.PSIsContainer }

        if ($sourceItems.Count -gt $destItems.Count) {
            $errors += "Not all files were copied. Source count: $($sourceItems.Count), Destination count: $($destItems.Count)"
        }
    }
    catch {
        $errors += "Failed to copy files from installation folder to service folder: $_"
    }

    return $errors
}
