function Invoke-DatabaseMigration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,
        
        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",
        
        [Parameter(Mandatory = $false)]
        [string]$ExeName = "",
        
        [Parameter(Mandatory = $false)]
        [string]$MigrationFile = "Migration.txt",
        
        [Parameter(Mandatory = $false)]
        [bool]$Down = $false
    )

    # Check prerequisites
    if (-not (Test-MigrationPrerequisites -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile)) {
        Write-ServiceLog -Level Error -Message "Migration prerequisites check failed."
        return $false
    }

    $exePath = Get-MigrationExecutable -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName
    Write-ServiceLog -Message "Using migration executable: $exePath"

    # Prepare command based on migration direction
    $command = ""
    $operationName = ""
    if ($Down) {
        $operationName = "Database migration DOWN"
        $command = "downFile:$MigrationFile"
        Write-ServiceLog -Message "Running database migration DOWN"
    } else {
        $operationName = "Database migration UP"
        $command = "up"
        Write-ServiceLog -Message "Running database migration UP"
    }

    # Execute migration command
    return Invoke-MigrationExecutable -Command $command -OperationName $operationName -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName
}
