# Function to extract migration state
function Invoke-ExtractMigrationState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,
 
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,
 
        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",
 
        [Parameter(Mandatory = $false)]
        [string]$ExeName = "",
 
        [Parameter(Mandatory = $false)]
        [string]$MigrationFile = "Migration.txt"
    )
 
    $exePath = Get-MigrationExecutable -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName
    Write-ServiceLog -Message "Using migration executable: $exePath"
 
    # Prepare command for migration extraction
    $operationName = "Migration state extraction"
 
    # Extract migration stores migration data into the service folder, not the installation folder.
    $migrationFolderPath = Join-Path -Path $ServiceFolder -ChildPath $SubFolder
    $migrationFilePath = Join-Path -Path $migrationFolderPath -ChildPath $MigrationFile
    $command = "extract:$migrationFilePath"
    Write-ServiceLog -Message "Extracting migration state to service folder"
 
    # Execute extraction command
    return Invoke-MigrationExecutable -Command $command -OperationName $operationName -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName
}
