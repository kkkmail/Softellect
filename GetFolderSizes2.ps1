param([string] $drive = "C:\")

. ./Scripts/Get-FolderSizes.ps1
Get-FolderSizes -drive $drive
