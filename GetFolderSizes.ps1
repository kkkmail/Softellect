param($InputFolder = "C:\", $FileMask = "*.*")

. ./Scripts/Get-FolderSizes.ps1
Get-FolderSizes -InputFolder $InputFolder -FileMask $FileMask
