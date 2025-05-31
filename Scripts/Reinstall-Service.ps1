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
. "$scriptDirectory\Invoke-ExtractMigrationState.ps1"
. "$scriptDirectory\ServiceReinstallErrorCodes.ps1"
. "$scriptDirectory\Get-StateFilePath.ps1"
. "$scriptDirectory\Update-ReinstallStateFile.ps1"
. "$scriptDirectory\Get-ReinstallStateFile.ps1"
. "$scriptDirectory\Clear-ReinstallStateFile.ps1"
. "$scriptDirectory\Update-StateAndExit.ps1"

# Output PowerShell version for diagnostics
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)"
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)"

# Main execution block
try {
    Write-ServiceLog -Message "Starting Windows service reinstallation..."

    # Clear any existing state file at the beginning
    $clearResult = Clear-ReinstallStateFile -ServiceFolder $ServiceFolder

    # Check admin rights
    if (-not (Test-AdminRights)) {
        Write-ServiceLog -Level Error -Message "Admin rights check failed. Terminating."
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_ADMIN_RIGHTS_REQUIRED -ErrorMessage "Administrator rights are required to perform service reinstallation."
    }

    # Resolve paths to absolute paths
    try {
        $ServiceFolder = Resolve-Path -Path $ServiceFolder -ErrorAction Stop
        $InstallationFolder = Resolve-Path -Path $InstallationFolder -ErrorAction Stop
    }
    catch {
        if ($ServiceFolder -and -not (Test-Path -Path $ServiceFolder)) {
            Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_SERVICE_FOLDER_INVALID -ErrorMessage "Service folder path is invalid: $ServiceFolder" -AdditionalInfo $_.Exception.Message
        }
        else {
            Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_INSTALLATION_FOLDER_INVALID -ErrorMessage "Installation folder path is invalid: $InstallationFolder" -AdditionalInfo $_.Exception.Message
        }
    }

    # Extract current migration state if migration is requested
    if ($PerformMigration) {
        Write-ServiceLog -Message "Extracting database migration state..."
        $extractResult = Invoke-ExtractMigrationState -ServiceFolder $ServiceFolder -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile
        if (-not $extractResult) {
            Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_MIGRATION_EXTRACT_FAILED -ErrorMessage "Failed to extract database migration state."
        }
    }

    # Check prerequisites
    Write-ServiceLog -Message "Checking prerequisites..."
    $prerequisitesResult = Test-ServicePrerequisites -ServiceFolder $ServiceFolder -InstallationFolder $InstallationFolder -InstallScriptName $InstallScriptName -UninstallScriptName $UninstallScriptName -MigrateScriptName $MigrateScriptName -PerformMigration $PerformMigration -SubFolder $SubFolder -MigrationFile $MigrationFile -ExeName $ExeName
    if (-not $prerequisitesResult) {
        Write-ServiceLog -Level Error -Message "Prerequisites check failed. Terminating."
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_PREREQUISITES_FAILED -ErrorMessage "Prerequisites check failed."
    }

    # Create backup
    Write-ServiceLog -Message "Creating service backup..."
    $backupFolder = Backup-Service -ServiceFolder $ServiceFolder

    if (-not $backupFolder) {
        Write-ServiceLog -Level Error -Message "Failed to create backup. Terminating."
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_BACKUP_FAILED -ErrorMessage "Failed to create backup of the service."
    }

    # Variable to track migration status
    $migrationPerformed = $false

    # Stop and uninstall service
    Write-ServiceLog -Message "Stopping and uninstalling service..."
    $uninstallResult = Uninstall-Service -ServiceFolder $ServiceFolder -UninstallScriptName $UninstallScriptName

    if (-not $uninstallResult) {
        Write-ServiceLog -Level Error -Message "Failed to uninstall service. Terminating."
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_UNINSTALL_FAILED -ErrorMessage "Failed to uninstall the service."
    }

    # Migrate database if PerformMigration is true
    if ($PerformMigration) {
        Write-ServiceLog -Message "Performing database migration..."
        $migrateResult = Invoke-DatabaseMigration -ServiceFolder $ServiceFolder -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile

        if (-not $migrateResult) {
            Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to migrate database." -InstallationFolder $InstallationFolder -MigrationPerformed $false -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
            Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_MIGRATION_UP_FAILED -ErrorMessage "Failed to perform database migration (UP)." -AdditionalInfo "Service has been rolled back to previous state."
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
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_CLEAR_FOLDER_FAILED -ErrorMessage "Failed to clear service folder." -AdditionalInfo "Service has been rolled back to previous state."
    }

    # Double check that service folder is empty
    $folderEmptyResult = Test-FolderEmpty -FolderPath $ServiceFolder
    if (-not $folderEmptyResult) {
        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Service folder is not empty after cleanup." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_FOLDER_NOT_EMPTY -ErrorMessage "Service folder is not empty after cleanup." -AdditionalInfo "Service has been rolled back to previous state."
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
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_COPY_INSTALLATION_FAILED -ErrorMessage "Failed to copy installation files." -AdditionalInfo "Service has been rolled back to previous state."
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
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_COPY_FILES_TO_KEEP_FAILED -ErrorMessage "Failed to copy files to keep." -AdditionalInfo "Service has been rolled back to previous state."
    }

    # Install service
    Write-ServiceLog -Message "Installing service..."
    $installResult = Install-Service -ServiceFolder $ServiceFolder -InstallScriptName $InstallScriptName

    if (-not $installResult) {
        Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Failed to install service." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_INSTALL_FAILED -ErrorMessage "Failed to install the service." -AdditionalInfo "Service has been rolled back to previous state."
    }

    # Success
    Write-ServiceLog -Message "Service reinstallation completed successfully!"

    # Clean up backup folder
    $cleanupResult = Remove-BackupFolder -BackupFolder $backupFolder

    # Update state file with success and exit
    Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_SUCCESS -ErrorMessage "Service reinstallation completed successfully." -AdditionalInfo "Migration performed: $PerformMigration"
}
catch {
    $exceptionMessage = $_.Exception.Message
    $exceptionDetails = $_.Exception.ToString()
    $stackTrace = $_.ScriptStackTrace

    Write-ServiceLog -Level Error -Message "Unexpected error: $exceptionMessage"
    Write-ServiceLog -Level Error -Message "Exception details: $exceptionDetails"

    # Create detailed error information for the state file
    $errorDetails = @{
        ExceptionMessage = $exceptionMessage
        ExceptionType = $_.Exception.GetType().FullName
        ScriptStackTrace = $stackTrace
        ExceptionDetails = $exceptionDetails
    }
    $errorDetailsJson = ($errorDetails | ConvertTo-Json -Depth 3 -Compress)

    # If we have a backup folder, try to restore
    if ($backupFolder -and (Test-Path -Path $backupFolder)) {
        Write-ServiceLog -Level Error -Message "Attempting rollback due to unexpected error..."
        $rollbackResult = Invoke-ServiceRollback -BackupFolder $backupFolder -ServiceFolder $ServiceFolder -FailureReason "Unexpected error occurred." -InstallationFolder $InstallationFolder -MigrationPerformed $migrationPerformed -PerformMigration $PerformMigration -InstallScriptName $InstallScriptName

        if ($rollbackResult) {
            Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_UNEXPECTED_EXCEPTION -ErrorMessage "Unexpected error occurred during reinstallation. Service has been rolled back to previous state." -AdditionalInfo $errorDetailsJson
        }
        else {
            Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_ROLLBACK_FAILED -ErrorMessage "Unexpected error occurred and rollback failed. Service may be in an inconsistent state." -AdditionalInfo $errorDetailsJson
        }
    }
    else {
        Write-ServiceLog -Level Error -Message "No backup available for rollback. Service may be in an inconsistent state."
        Update-StateAndExit -ServiceFolder $ServiceFolder -ErrorCode $global:ERROR_ROLLBACK_FAILED -ErrorMessage "Unexpected error occurred and no backup available for rollback. Service may be in an inconsistent state." -AdditionalInfo $errorDetailsJson
    }
}
