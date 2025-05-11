function Clear-ServiceFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder
    )

    $errors = @()

    try {
        # First attempt to delete everything recursively in one go
        try {
            Write-ServiceLog -Message "Attempting to delete all files and folders recursively..."
            # Use -Force to delete hidden and read-only files, -Recurse to delete subfolders
            Remove-Item -Path "$ServiceFolder\*" -Force -Recurse -ErrorAction Stop

            # If we get here, the deletion was successful
            Write-ServiceLog -Message "All files and folders deleted successfully."
        }
        catch {
            Write-ServiceLog -Level Warning -Message "Bulk deletion failed, falling back to individual file deletion."

            # If bulk delete fails, get all files first, then folders, and delete them individually
            # Get all files first (not folders)
            $files = Get-ChildItem -Path $ServiceFolder -Recurse -File

            # Delete each file individually
            foreach ($file in $files) {
                try {
                    Write-ServiceLog -Level Info -Message "Deleting file: $($file.FullName)"
                    Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                }
                catch {
                    $errors += "Failed to delete file '$($file.FullName)': $_"
                }
            }

            # Now get all folders (from deepest to root)
            $folders = Get-ChildItem -Path $ServiceFolder -Recurse -Directory |
                        Sort-Object -Property FullName -Descending

            # Delete each folder individually (from deepest to shallowest)
            foreach ($folder in $folders) {
                try {
                    Write-ServiceLog -Level Info -Message "Deleting folder: $($folder.FullName)"
                    Remove-Item -Path $folder.FullName -Force -Recurse -ErrorAction Stop
                }
                catch {
                    $errors += "Failed to delete folder '$($folder.FullName)': $_"
                }
            }
        }

        # Check if folder is empty after all deletion attempts
        $remainingItems = Get-ChildItem -Path $ServiceFolder -Force
        if ($remainingItems) {
            Write-ServiceLog -Level Warning -Message "Service folder still contains items after cleanup."
            foreach ($item in $remainingItems) {
                Write-ServiceLog -Level Warning -Message "Remaining item: $($item.FullName)"
            }
            $errors += "Service folder is not empty after cleanup."
        }
    }
    catch {
        $errors += "Error during service folder cleanup: $_"
    }

    return $errors
}
