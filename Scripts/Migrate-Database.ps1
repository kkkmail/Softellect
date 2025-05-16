# Perform database migration
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$InstallationFolder,

    [Parameter(Mandatory = $false)]
    [string]$SubFolder = "Migrations",

    [Parameter(Mandatory = $false)]
    [string]$ExeName = "",

    [Parameter(Mandatory = $false)]
    [bool]$Down = $false,

    [Parameter(Mandatory = $false)]
    [string]$MigrationFile = "Migration.txt"
)

# Get the script directory
$scriptDirectory = $PSScriptRoot

# Dot-source required functions
. "$scriptDirectory\Write-ServiceLog.ps1"
. "$scriptDirectory\Test-MigrationPrerequisites.ps1"
. "$scriptDirectory\Get-MigrationExecutable.ps1"
. "$scriptDirectory\Invoke-MigrationExecutable.ps1"
. "$scriptDirectory\Invoke-MigrationVerification.ps1"
. "$scriptDirectory\Invoke-DatabaseMigration.ps1"

# Main execution
try {
    Write-ServiceLog -Message "Starting database migration process..."

    # Resolve the installation folder path
    $InstallationFolder = Resolve-Path -Path $InstallationFolder -ErrorAction Stop

    # Check prerequisites
    Write-ServiceLog -Message "Checking prerequisites..."
    if (-not (Test-MigrationPrerequisites -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile)) {
        Write-ServiceLog -Level Error -Message "Prerequisites check failed. Terminating."
        Exit 1
    }

    # Verify the migration
    Write-ServiceLog -Message "Verifying migration file..."
    $verificationResult = Invoke-MigrationVerification -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile

    if (-not $verificationResult) {
        Write-ServiceLog -Level Error -Message "Migration verification failed. Terminating."
        Exit 1
    }

    # Run the migration
    Write-ServiceLog -Message "Running database migration..."
    $result = Invoke-DatabaseMigration -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile -Down $Down

    if (-not $result) {
        Write-ServiceLog -Level Error -Message "Database migration failed. Terminating."
        Exit 1
    }

    # Success
    Write-ServiceLog -Message "Database migration completed successfully!"
    Exit 0
}
catch
{
    Write-ServiceLog -Level Error -Message "Unexpected error during migration: $_"
    Exit 1
}
