[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$ServiceFolder,

    [Parameter(Mandatory = $true)]
    [string]$InstallationFolder,

    [Parameter(Mandatory = $false)]
    [string]$InstallScriptName = "Install-WorkerNodeService.ps1",

    [Parameter(Mandatory = $false)]
    [string]$UninstallScriptName = "Uninstall-WorkerNodeService.ps1",

    [Parameter(Mandatory = $false)]
    [string]$MigrateScriptName = "Migrate-Database.ps1",

    [Parameter(Mandatory = $false)]
    [bool]$PerformMigration = $false,

    [Parameter(Mandatory = $false)]
    [string[]]$FilesToKeep = @("appsettings.json"),

    [Parameter(Mandatory = $false)]
    [string]$SubFolder = "Migrations",

    [Parameter(Mandatory = $false)]
    [string]$MigrationFile = "Migration.txt",

    [Parameter(Mandatory = $false)]
    [string]$ExeName = ""
)

# Get the script directory
$scriptDirectory = $PSScriptRoot

# Dot-source all required functions
. "$scriptDirectory\Write-ServiceLog.ps1"
. "$scriptDirectory\Test-AdminRights.ps1"
. "$scriptDirectory\Test-ServicePrerequisites.ps1"
. "$scriptDirectory\Backup-Service.ps1"
. "$scriptDirectory\Uninstall-Service.ps1"
. "$scriptDirectory\Clear-ServiceFolder.ps1"
. "$scriptDirectory\Test-FolderEmpty.ps1"
. "$scriptDirectory\Copy-InstallationToService.ps1"
. "$scriptDirectory\Copy-FilesToKeep.ps1"
. "$scriptDirectory\Install-Service.ps1"
. "$scriptDirectory\Remove-BackupFolder.ps1"
. "$scriptDirectory\Restore-ServiceFromBackup.ps1"
. "$scriptDirectory\Test-MigrationPrerequisites.ps1"
. "$scriptDirectory\Get-MigrationExecutable.ps1"
. "$scriptDirectory\Invoke-MigrationExecutable.ps1"
. "$scriptDirectory\Invoke-MigrationVerification.ps1"
. "$scriptDirectory\Invoke-DatabaseMigration.ps1"
. "$scriptDirectory\Invoke-ServiceRollback.ps1"

# Output PowerShell version for diagnostics
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)"
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)"

# Main execution block
try {
    Write-ServiceLog -Message "Starting Windows service reinstallation..."

    # Check admin rights
    if (-not (Test-AdminRights)) {
        Write-ServiceLog -Level Error -Message "Admin rights check failed. Terminating."
        Exit 1
    }

    # Resolve paths to absolute paths
    $ServiceFolder = Resolve-Path -Path $ServiceFolder -ErrorAction Stop
    $InstallationFolder = Resolve-Path -Path $InstallationFolder -ErrorAction Stop

    # Check prerequisites
    Write-ServiceLog -Message "Checking prerequisites..."
    if (-not (Test-ServicePrerequisites -ServiceFolder $ServiceFolder -InstallationFolder $InstallationFolder -InstallScriptName $InstallScriptName -UninstallScriptName $UninstallScriptName -MigrateScriptName $MigrateScriptName -PerformMigration $PerformMigration -SubFolder $SubFolder -MigrationFile $MigrationFile -ExeName $ExeName)) {
        Write-ServiceLog -Level Error -Message "Prerequisites check failed. Terminating."
        Exit 1
    }

    # Create backup
    Write-ServiceLog -Message "Creating service backup..."
    $backupFolder = Backup-Service -ServiceFolder $ServiceFolder

    if (-not $backupFolder) {
        Write-ServiceLog -Level Error -Message "Failed to create backup. Terminating."
        Exit 1
    }

    # Variable to track migration status
    $migrationPerformed = $false

    # Stop and uninstall service
    Write-ServiceLog -Message "Stopping and uninstalling service..."
    $uninstallResult = Uninstall-Service -ServiceFolder $ServiceFolder -UninstallScriptName $UninstallScriptName

    if (-not $uninstallResult) {
        Write-ServiceLog -Level Error -Message "Failed to uninstall service. Terminating."
        Exit 1
    }

    # Migrate database if PerformMigration is true
    if ($PerformMigration) {
        Write-ServiceLog -Message "Performing database migration..."
        $migrateResult = Invoke-DatabaseMigration -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile

        if (-not $migrateResult) {
            Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to migrate database." -InstallationFolder $InstallationFolder -MigrationPerformed $false -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
            Exit 1
        }

        $migrationPerformed = $true
        Write-ServiceLog -Message "Database migration completed successfully."
    }

    # Clear service folder
    Write-ServiceLog -Message "Clearing service folder..."
    $clearErrors = Clear-ServiceFolder -ServiceFolder $ServiceFolder

    # Filter out "not found" errors since they're not actual failures
    $realErrors = $clearErrors | Where-Object { $_ -notmatch "because it does not exist" }

    if ($realErrors -and $realErrors.Count -gt 0) {
        Write-ServiceLog -Level Error -Message "Errors during service folder cleanup:"
        foreach ($err in $realErrors) {
            Write-ServiceLog -Level Error -Message " - $err"
        }

        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to clear service folder." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Exit 1
    }

    # Double check that service folder is empty
    if (-not (Test-FolderEmpty -FolderPath $ServiceFolder)) {
        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Service folder is not empty after cleanup." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Exit 1
    }

    # Copy files from installation folder to service folder
    Write-ServiceLog -Message "Copying files from installation folder to service folder..."
    $copyErrors = Copy-InstallationToService -InstallationFolder $InstallationFolder -ServiceFolder $ServiceFolder

    if ($copyErrors) {
        Write-ServiceLog -Level Error -Message "Errors during file copy:"
        foreach ($err in $copyErrors) {
            Write-ServiceLog -Level Error -Message " - $err"
        }

        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to copy installation files." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Exit 1
    }

    # Copy files to keep from backup
    Write-ServiceLog -Message "Copying files to keep from backup..."
    $keepErrors = Copy-FilesToKeep -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FilesToKeep $FilesToKeep

    if ($keepErrors) {
        Write-ServiceLog -Level Error -Message "Errors during copying files to keep:"
        foreach ($err in $keepErrors) {
            Write-ServiceLog -Level Error -Message " - $err"
        }

        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to copy files to keep." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Exit 1
    }

    # Install service
    Write-ServiceLog -Message "Installing service..."
    $installResult = Install-Service -ServiceFolder $ServiceFolder -InstallScriptName $InstallScriptName

    if (-not $installResult) {
        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to install service." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Exit 1
    }

    # Success
    Write-ServiceLog -Message "Service reinstallation completed successfully!"

    # Clean up backup folder
    Remove-BackupFolder -BackupFolder $backupFolder

    Exit 0
}
catch {
    Write-ServiceLog -Level Error -Message "Unexpected error: $_"

    # If we have a backup folder, try to restore
    if ($backupFolder -and (Test-Path -Path $backupFolder)) {
        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Unexpected error occurred." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
    }
    else {
        Write-ServiceLog -Level Error -Message "No backup available for rollback. Service may be in an inconsistent state."
    }

    Exit 1
}
