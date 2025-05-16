function Get-FolderSizes {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string]$Drive = "C:\"
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Import-Functions.ps1"
    Import-Functions

    Write-ServiceLog -Message "Processing input folder: $($Drive)"

    # Initialize arrays to store folder sizes and inaccessible folders
    $folderSizes = @()
    $inaccessibleFolders = @()

    # Get top-level folders first to prevent immediate termination on inaccessible folders
    $topLevelFolders = Get-ChildItem -Path $Drive -Directory -Force -ErrorAction SilentlyContinue

    foreach ($topFolder in $topLevelFolders) {
        Write-ServiceLog -Message "Processing top-level folder: $($topFolder.FullName)"

        try {
            # Recursively get all subfolders for the current top-level folder
            $folders = Get-ChildItem -Path $topFolder.FullName -Directory -Recurse -Force -ErrorAction SilentlyContinue
            $folders += $topFolder  # Include the top-level folder itself

            foreach ($folder in $folders) {
                Write-ServiceLog -Message "Processing folder: $($folder.FullName)"

                try {
                    # Get the total size of all files in the folder (excluding subfolders)
                    $size = (Get-ChildItem -Path $folder.FullName -File -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum

                    # Add the folder and its size to the array
                    $folderSizes += [PSCustomObject]@{
                        FolderName = $folder.FullName
                        TotalSize  = $size
                    }
                } catch {
                    # Collect inaccessible folders and report error
                    Write-ServiceLog -Message "Could not access folder: $($folder.FullName)" -Level "Error"
                    $inaccessibleFolders += $folder.FullName
                }
            }
        } catch {
            # Collect inaccessible top-level folders and report error
            Write-ServiceLog -Message "Could not access top-level folder: $($topFolder.FullName)" -Level "Error"
            $inaccessibleFolders += $topFolder.FullName
        }
    }

    # Sort the results by TotalSize in descending order
    $sortedResults = $folderSizes | Sort-Object -Property TotalSize -Descending

    # Display the table with TotalSize padded and FolderName
    $sortedResults | ForEach-Object {
        $formattedOutput = "{0,12:N0}  {1}" -f $_.TotalSize, $_.FolderName
        Write-ServiceLog -Message $formattedOutput
    }

    # Report inaccessible folders
    if ($inaccessibleFolders.Count -gt 0) {
        Write-ServiceLog -Message "Inaccessible Folders:" -Level "Warning"
        $inaccessibleFolders | ForEach-Object {
            Write-ServiceLog -Message "$_" -Level "Error"
        }
    }

    # Return the results for potential further processing
    return $sortedResults
}
