# Function to find the migration executable
function Get-MigrationExecutable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,

        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",

        [Parameter(Mandatory = $false)]
        [string]$ExeName = ""
    )

    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder

    if (-not [string]::IsNullOrEmpty($ExeName)) {
        return Join-Path -Path $migrationFolderPath -ChildPath $ExeName
    } else {
        $migrationExes = Get-ChildItem -Path $migrationFolderPath -Filter "*Migrations*.exe"
        return $migrationExes[0].FullName
    }
}
