# Function to verify the migration in the file
function Invoke-MigrationVerification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,

        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",

        [Parameter(Mandatory = $false)]
        [string]$ExeName = "",

        [Parameter(Mandatory = $false)]
        [string]$MigrationFile = "Migration.txt"
    )

    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder
    $migrationFilePath = Join-Path -Path $migrationFolderPath -ChildPath $MigrationFile

    Write-ServiceLog -Message "Verifying migration in file: $migrationFilePath"

    # Read the migration name from the file
    $migrationName = Get-Content -Path $migrationFilePath -Raw
    $migrationName = $migrationName.Trim()

    Write-ServiceLog -Message "Verifying migration: $migrationName"

    # Execute verification command
    return Invoke-MigrationExecutable -Command "verifyFile:$migrationFilePath" -OperationName "Migration verification" -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName
}
