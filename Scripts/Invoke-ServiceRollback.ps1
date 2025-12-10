# Function to perform rollback in case of failure
function Invoke-ServiceRollback {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder,

        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,

        [Parameter(Mandatory = $true)]
        [string]$FailureReason,

        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,

        [Parameter(Mandatory = $false)]
        [bool]$MigrationPerformed = $false,

        [Parameter(Mandatory = $false)]
        [bool]$PerformMigration = $false,

        [Parameter(Mandatory = $false)]
        [string]$InstallScriptName = "Install-WorkerNodeService.ps1"
    )

    Write-ServiceLog -Level Error -Message "FAILURE: $FailureReason"
    Write-ServiceLog -Level Warning -Message "Performing rollback..."

    # Restore files from backup
    $restoreErrors = Restore-ServiceFromBackup -BackupFolder $BackupFolder -ServiceFolder $ServiceFolder

    if ($restoreErrors) {
        Write-ServiceLog -Level Error -Message "Errors during rollback:"
        foreach ($err in $restoreErrors) {
            Write-ServiceLog -Level Error -Message " - $err"
        }

        return $false
    }

    # If migration was performed and we need to roll back, try to migrate down
    if ($MigrationPerformed -and $PerformMigration) {
        Write-ServiceLog -Level Warning -Message "Attempting to roll back database migration..."
        $migrateDownResult = Invoke-DatabaseMigration -ServiceFolder $ServiceFolder -InstallationFolder $InstallationFolder -Down $true

        if (-not $migrateDownResult) {
            Write-ServiceLog -Level Error -Message "Failed to roll back database migration. Manual intervention required."
            Write-ServiceLog -Level Error -Message "The system is in an inconsistent state. Please restore the database manually."
            return $false
        }

        Write-ServiceLog -Level Warning -Message "Database migration rolled back successfully."
    }

    # Try to install the service again
    $installResult = Install-Service -ServiceFolder $ServiceFolder -InstallScriptName $InstallScriptName

    if (-not $installResult) {
        Write-ServiceLog -Level Error -Message "Failed to reinstall service during rollback."
        return $false
    }

    Write-ServiceLog -Level Warning -Message "Rollback completed. Original service has been restored."
    return $true
}
