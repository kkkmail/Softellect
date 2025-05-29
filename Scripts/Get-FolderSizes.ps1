function Get-FolderSizes {
  [CmdletBinding()]
  param (
      [Parameter(Mandatory = $false)]
      [string]$InputFolder = "C:\",
     
      [Parameter(Mandatory = $false)]
      [string]$FileMask = "*.*",
      
      [Parameter(Mandatory = $false)]
      [string[]]$TextFileTypes = @('asm', 'bat', 'c', 'cfg', 'cmd', 'conf', 'config', 'cpp', 'cs', 'csproj', 'css', 'dart', 'dpr', 'fs', 'fsproj', 'go', 'h', 'hpp', 'htm', 'html', 'inc', 'ini', 'java', 'js', 'json', 'jsonc', 'jsx', 'kt', 'less', 'log', 'lua', 'md', 'pas', 'php', 'pl', 'props', 'ps1', 'psd1', 'psm1', 'py', 'r', 'rb', 'rs', 'ruleset', 's', 'sass', 'scala', 'scss', 'settings', 'sh', 'sln', 'sql', 'svg', 'svelte', 'swift', 'targets', 'toml', 'ts', 'tsx', 'txt', 'vb', 'vbproj', 'vcxproj', 'vue', 'xml', 'xsd', 'xsl', 'xslt', 'yaml', 'yml')
  )

  # Get the script directory and load dependencies
  $scriptDirectory = $PSScriptRoot
   . "$scriptDirectory\Write-ServiceLog.ps1"

  Write-ServiceLog -Message "Processing input folder: $($InputFolder) with file mask: $($FileMask)"

  # Initialize arrays to store folder sizes, file extensions, LOC data, and inaccessible folders
  $folderSizes = @()
  $fileExtensions = @{}
  $locData = @{}
  $inaccessibleFolders = @()
  
  # Convert text file types to lowercase for comparison
  $textFileTypesLower = $TextFileTypes | ForEach-Object { $_.ToLower() }

  # Get top-level folders first to prevent immediate termination on inaccessible folders
  $topLevelFolders = Get-ChildItem -Path $InputFolder -Directory -Force -ErrorAction SilentlyContinue

  foreach ($topFolder in $topLevelFolders) {
      Write-ServiceLog -Message "Processing top-level folder: $($topFolder.FullName)"

      try {
          # Recursively get all subfolders for the current top-level folder
          $folders = Get-ChildItem -Path $topFolder.FullName -Directory -Recurse -Force -ErrorAction SilentlyContinue
          $folders += $topFolder  # Include the top-level folder itself

          foreach ($folder in $folders) {
              Write-ServiceLog -Message "Processing folder: $($folder.FullName)"

              try {
                  # Get all files in the folder (excluding subfolders) matching the file mask
                  $files = Get-ChildItem -Path $folder.FullName -File -Filter $FileMask -Force -ErrorAction SilentlyContinue
                  
                  # Calculate total size for folder
                  $size = ($files | Measure-Object -Property Length -Sum).Sum

                  # Add the folder and its size to the array
                  $folderSizes += [PSCustomObject]@{
                      FolderName = $folder.FullName
                      TotalSize  = $size
                  }
                  
                  # Process file extensions and LOC for text files
                  foreach ($file in $files) {
                      $extension = if ($file.Extension) { 
                          $file.Extension.TrimStart('.').ToLower() 
                      } else { 
                          'no extension' 
                      }
                      
                      # Track file sizes by extension
                      if ($fileExtensions.ContainsKey($extension)) {
                          $fileExtensions[$extension] += $file.Length
                      } else {
                          $fileExtensions[$extension] = $file.Length
                      }
                      
                      # Calculate LOC for text files
                      if ($textFileTypesLower -contains $extension) {
                          try {
                              $content = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
                              $lineCount = if ($content) { $content.Count } else { 0 }
                              
                              if ($locData.ContainsKey($extension)) {
                                  $locData[$extension] += $lineCount
                              } else {
                                  $locData[$extension] = $lineCount
                              }
                          } catch {
                              Write-ServiceLog -Message "Could not read file for LOC: $($file.FullName)" -Level "Warning"
                          }
                      }
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
  Write-ServiceLog -Message "Folder Sizes:"
  $sortedResults | ForEach-Object {
      $formattedOutput = "{0,12:N0}  {1}" -f $_.TotalSize, $_.FolderName
      Write-ServiceLog -Message $formattedOutput
  }
  
  # Display unified file type table with size and LOC
  if ($fileExtensions.Count -gt 0) {
      Write-ServiceLog -Message " "
      Write-ServiceLog -Message "File Type Summary:"
      Write-ServiceLog -Message ("{0,-15} {1,12} {2,12}" -f "Extension", "Total Size", "LOC")
      Write-ServiceLog -Message ("-" * 40)
      
      $sortedExtensions = $fileExtensions.GetEnumerator() | Sort-Object -Property Name
      $sortedExtensions | ForEach-Object {
          $extension = $_.Key
          $size = $_.Value
          $loc = if ($locData.ContainsKey($extension)) { $locData[$extension] } else { $null }
          
          if ($loc -ne $null) {
              $formattedOutput = "{0,-15} {1,12:N0} {2,12:N0}" -f $extension, $size, $loc
          } else {
              $formattedOutput = "{0,-15} {1,12:N0} {2,12}" -f $extension, $size, "-"
          }
          Write-ServiceLog -Message $formattedOutput
      }
  }

  # Report inaccessible folders
  if ($inaccessibleFolders.Count -gt 0) {
      Write-ServiceLog -Message " "
      Write-ServiceLog -Message "Inaccessible Folders:" -Level "Warning"
      $inaccessibleFolders | ForEach-Object {
          Write-ServiceLog -Message "$_" -Level "Error"
      }
  }

  # Return the results for potential further processing
  return $sortedResults
}
