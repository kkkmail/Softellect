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
    [string]$MigrateScriptName = "Migrate-WorkerNodeService.ps1",

    [Parameter(Mandatory = $false)]
    [bool]$PerformMigration = $false,

    [Parameter(Mandatory = $false)]
    [string[]]$FilesToKeep = @("appsettings.json")
)

# Output PowerShell version for diagnostics
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)"
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)"

# Check if we have the necessary permissions
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$hasAdminRights = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Check if we can access the service control manager
$canManageServices = $false
try {
    $null = Get-Service -Name DoesNotExist -ErrorAction Stop
    $canManageServices = $true
} catch [System.Management.Automation.RuntimeException] {
    if ($_.Exception.Message -like "*Access is denied*") {
        $canManageServices = $false
    } else {
        $canManageServices = $true
    }
}

# If we don't have admin rights but can manage services (like LOCAL SERVICE can), continue
if (-not $hasAdminRights -and $canManageServices) {
    Write-Host "Running with service management permissions (though not full administrator)"
} elseif (-not $hasAdminRights) {
    throw "This script requires administrator privileges or service management permissions to run."
}

# Log function to handle formatted output
function Write-Log {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $false)]
        [ValidateSet("Info", "Warning", "Error")]
        [string]$Level = "Info"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $formattedMessage = "[$timestamp] [$Level] $Message"

    switch ($Level) {
        "Info" { Write-Host $formattedMessage -ForegroundColor White }
        "Warning" { Write-Host $formattedMessage -ForegroundColor Yellow }
        "Error" { Write-Host $formattedMessage -ForegroundColor Red }
    }
}

# Function to validate input folders and scripts
function Test-Prerequisites {
    [CmdletBinding()]
    param()

    $prerequisites = $true

    # Check if service folder exists
    if (-not (Test-Path -Path $ServiceFolder -PathType Container)) {
        Write-Log -Level Error -Message "Service folder '$ServiceFolder' does not exist."
        $prerequisites = $false
    }

    # Check if installation folder exists
    if (-not (Test-Path -Path $InstallationFolder -PathType Container)) {
        Write-Log -Level Error -Message "Installation folder '$InstallationFolder' does not exist."
        $prerequisites = $false
    }

    # Check if install script exists in service folder
    if (-not (Test-Path -Path (Join-Path -Path $ServiceFolder -ChildPath $InstallScriptName))) {
        Write-Log -Level Error -Message "Install script '$InstallScriptName' not found in service folder."
        $prerequisites = $false
    }

    # Check if uninstall script exists in service folder
    if (-not (Test-Path -Path (Join-Path -Path $ServiceFolder -ChildPath $UninstallScriptName))) {
        Write-Log -Level Error -Message "Uninstall script '$UninstallScriptName' not found in service folder."
        $prerequisites = $false
    }

    # Check if install script exists in installation folder
    if (-not (Test-Path -Path (Join-Path -Path $InstallationFolder -ChildPath $InstallScriptName))) {
        Write-Log -Level Error -Message "Install script '$InstallScriptName' not found in installation folder."
        $prerequisites = $false
    }

    # Check if migrate script exists in installation folder when PerformMigration is true
    if ($PerformMigration -and -not (Test-Path -Path (Join-Path -Path $InstallationFolder -ChildPath $MigrateScriptName))) {
        Write-Log -Level Error -Message "Migrate script '$MigrateScriptName' not found in installation folder."
        $prerequisites = $false
    }

    return $prerequisites
}

# Function to create backup of service
function Backup-Service {
    [CmdletBinding()]
    param()

    try {
        # Create a unique backup folder name
        $backupFolder = Join-Path -Path $env:TEMP -ChildPath "ServiceBackup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Write-Log -Message "Creating backup in: $backupFolder"

        # Create the backup folder
        New-Item -Path $backupFolder -ItemType Directory -Force | Out-Null

        # Copy all files and folders from service folder to backup
        Copy-Item -Path "$ServiceFolder\*" -Destination $backupFolder -Recurse -Force

        # Verify backup was successful
        $sourceItems = (Get-ChildItem -Path $ServiceFolder -Recurse | Measure-Object).Count
        $backupItems = (Get-ChildItem -Path $backupFolder -Recurse | Measure-Object).Count

        if ($sourceItems -ne $backupItems) {
            Write-Log -Level Warning -Message "Backup item count mismatch. Source: $sourceItems, Backup: $backupItems"
        }

        Write-Log -Message "Backup created successfully."
        return $backupFolder
    }
    catch {
        Write-Log -Level Error -Message "Failed to create backup: $_"
        return $null
    }
}

# Function to stop and uninstall service
function Uninstall-Service {
    [CmdletBinding()]
    param()

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to service folder
        Set-Location -Path $ServiceFolder

        # Run uninstall script
        Write-Log -Message "Running uninstall script: $UninstallScriptName"
        $uninstallScript = Join-Path -Path $ServiceFolder -ChildPath $UninstallScriptName

        # Execute uninstall script and capture its output
        $hadError = $false

        try {
            $output = & $uninstallScript 2>&1

            # If the script ran without throwing, check its output for error messages
            if ($output -match "error|fail|exception" -and $output -notmatch "uninstalled service") {
                $hadError = $true
                Write-Log -Level Error -Message "Uninstall script reported errors in its output."
            }
        }
        catch {
            # This captures errors thrown by the script
            Write-Log -Level Error -Message "Uninstall script threw an exception: $_"
            $hadError = $true
        }

        # Verify service is uninstalled by checking if it still exists
        $serviceName = $null

        # Try to extract service name from the script content
        try {
            $scriptContent = Get-Content -Path $uninstallScript -Raw
            if ($scriptContent -match "service:\s+([a-zA-Z0-9_\-]+)") {
                $serviceName = $matches[1]
            }
        }
        catch {
            Write-Log -Level Warning -Message "Could not read uninstall script content to extract service name."
        }

        # If we can't extract the service name from the script, look for clues in the output
        if (-not $serviceName -and $output -match "service:\s+([a-zA-Z0-9_\-]+)") {
            $serviceName = $matches[1]
        }

        if ($serviceName -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            Write-Log -Level Error -Message "Service '$serviceName' is still present after uninstall script execution."
            $hadError = $true
        }

        # Restore original location
        Set-Location -Path $currentLocation

        if ($hadError) {
            Write-Log -Level Error -Message "Uninstall output: $output"
            return $false
        }

        Write-Log -Message "Service uninstalled successfully."
        return $true
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation
        Write-Log -Level Error -Message "Failed to uninstall service: $_"
        return $false
    }
}

# Function to migrate database
function Invoke-DatabaseMigration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [bool]$Down = $false
    )

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to installation folder
        Set-Location -Path $InstallationFolder

        # Run migrate script
        $direction = if ($Down) { "down" } else { "up" }
        Write-Log -Message "Running migrate script ($direction): $MigrateScriptName"
        $migrateScript = Join-Path -Path $InstallationFolder -ChildPath $MigrateScriptName

        # Execute migrate script and capture its output
        $hadError = $false

        try {
            $output = if ($Down) {
                & $migrateScript -Down $true 2>&1
            } else {
                & $migrateScript 2>&1
            }

            # If the script ran without throwing, check its output for error messages
            if ($output -match "error|fail|exception" -and $output -notmatch "migration (completed|successful)") {
                $hadError = $true
                Write-Log -Level Error -Message "Migration script reported errors in its output."
            }
        }
        catch {
            # This captures errors thrown by the script
            Write-Log -Level Error -Message "Migration script threw an exception: $_"
            $hadError = $true
        }

        # Restore original location
        Set-Location -Path $currentLocation

        if ($hadError) {
            Write-Log -Level Error -Message "Migration output: $output"
            return $false
        }

        Write-Log -Message "Database migration ($direction) completed successfully."
        return $true
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation
        Write-Log -Level Error -Message "Failed to migrate database: $_"
        return $false
    }
}

# Function to clear service folder
function Clear-ServiceFolder {
    [CmdletBinding()]
    param()

    $errors = @()

    try {
        # First attempt to delete everything recursively in one go
        try {
            Write-Log -Message "Attempting to delete all files and folders recursively..."
            # Use -Force to delete hidden and read-only files, -Recurse to delete subfolders
            Remove-Item -Path "$ServiceFolder\*" -Force -Recurse -ErrorAction Stop

            # If we get here, the deletion was successful
            Write-Log -Message "All files and folders deleted successfully."
        }
        catch {
            Write-Log -Level Warning -Message "Bulk deletion failed, falling back to individual file deletion."

            # If bulk delete fails, get all files first, then folders, and delete them individually
            # Get all files first (not folders)
            $files = Get-ChildItem -Path $ServiceFolder -Recurse -File

            # Delete each file individually
            foreach ($file in $files) {
                try {
                    Write-Log -Level Info -Message "Deleting file: $($file.FullName)"
                    Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                }
                catch {
                    $errors += "Failed to delete file '$($file.FullName)': $_"
                }
            }

            # Now get all folders (from deepest to root)
            $folders = Get-ChildItem -Path $ServiceFolder -Recurse -Directory |
                        Sort-Object -Property FullName -Descending

            # Delete each folder individually (from deepest to shallowest)
            foreach ($folder in $folders) {
                try {
                    Write-Log -Level Info -Message "Deleting folder: $($folder.FullName)"
                    Remove-Item -Path $folder.FullName -Force -Recurse -ErrorAction Stop
                }
                catch {
                    $errors += "Failed to delete folder '$($folder.FullName)': $_"
                }
            }
        }

        # Check if folder is empty after all deletion attempts
        $remainingItems = Get-ChildItem -Path $ServiceFolder -Force
        if ($remainingItems) {
            Write-Log -Level Warning -Message "Service folder still contains items after cleanup."
            foreach ($item in $remainingItems) {
                Write-Log -Level Warning -Message "Remaining item: $($item.FullName)"
            }
            $errors += "Service folder is not empty after cleanup."
        }
    }
    catch {
        $errors += "Error during service folder cleanup: $_"
    }

    return $errors
}

# Function to verify service folder is empty
function Test-FolderEmpty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FolderPath
    )

    $items = Get-ChildItem -Path $FolderPath -Force -ErrorAction SilentlyContinue
    return ($null -eq $items -or $items.Count -eq 0)
}

# Function to restore service from backup
function Restore-ServiceFromBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder
    )

    $errors = @()

    try {
        # Clear service folder first
        $clearErrors = Clear-ServiceFolder
        if ($clearErrors) {
            $errors += $clearErrors
            Write-Log -Level Warning -Message "Could not fully clear service folder before restore."
        }

        # Check if backup folder exists
        if (-not (Test-Path -Path $BackupFolder)) {
            $errors += "Backup folder '$BackupFolder' does not exist."
            return $errors
        }

        # Copy all files from backup to service folder
        Write-Log -Message "Copying files from backup folder to service folder..."
        try {
            # Try bulk copy first
            Copy-Item -Path "$BackupFolder\*" -Destination $ServiceFolder -Recurse -Force -ErrorAction Stop
            Write-Log -Message "All files restored from backup successfully."
        }
        catch {
            Write-Log -Level Warning -Message "Bulk restore failed, falling back to individual file restore: $_"

            # If bulk copy fails, copy files individually
            $files = Get-ChildItem -Path $BackupFolder -Recurse

            foreach ($file in $files) {
                try {
                    # Get the relative path from backup folder
                    $relativePath = $file.FullName.Substring($BackupFolder.Length)
                    $destinationPath = Join-Path -Path $ServiceFolder -ChildPath $relativePath

                    # If it's a directory, create it
                    if ($file.PSIsContainer) {
                        if (-not (Test-Path -Path $destinationPath)) {
                            New-Item -Path $destinationPath -ItemType Directory -Force | Out-Null
                        }
                        Write-Log -Message "Created directory during restore: $destinationPath"
                    }
                    # If it's a file, copy it
                    else {
                        # Ensure the destination directory exists
                        $destinationDir = Split-Path -Path $destinationPath -Parent
                        if (-not (Test-Path -Path $destinationDir)) {
                            New-Item -Path $destinationDir -ItemType Directory -Force | Out-Null
                            Write-Log -Message "Created directory during restore: $destinationDir"
                        }

                        # Copy the file
                        Copy-Item -Path $file.FullName -Destination $destinationPath -Force
                        Write-Log -Message "Restored file: $($file.FullName) to $destinationPath"
                    }
                }
                catch {
                    $errors += "Failed to restore '$($file.FullName)': $_"
                }
            }
        }

        # Verify all files were restored correctly
        $backupItems = Get-ChildItem -Path $BackupFolder -Recurse | Where-Object { -not $_.PSIsContainer }
        $restoredItems = Get-ChildItem -Path $ServiceFolder -Recurse | Where-Object { -not $_.PSIsContainer }

        if ($backupItems.Count -gt $restoredItems.Count) {
            $errors += "Not all files were restored. Backup count: $($backupItems.Count), Restored count: $($restoredItems.Count)"
        }
    }
    catch {
        $errors += "Failed to restore service from backup: $_"
    }

    return $errors
}

# Function to install service
function Install-Service {
    [CmdletBinding()]
    param()

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to service folder
        Set-Location -Path $ServiceFolder

        # Run install script
        Write-Log -Message "Running install script: $InstallScriptName"
        $installScript = Join-Path -Path $ServiceFolder -ChildPath $InstallScriptName

        # Execute install script and capture its output
        $hadError = $false

        try {
            $output = & $installScript 2>&1

            # If the script ran without throwing, check its output for error messages
            if ($output -match "error|fail|exception" -and $output -notmatch "started service") {
                $hadError = $true
                Write-Log -Level Error -Message "Install script reported errors in its output."
            }
        }
        catch {
            # This captures errors thrown by the script
            Write-Log -Level Error -Message "Install script threw an exception: $_"
            $hadError = $true
        }

        # Verify service is installed by checking if it exists and is running
        $serviceName = $null

        # Try to extract service name from the script content
        try {
            $scriptContent = Get-Content -Path $installScript -Raw
            if ($scriptContent -match "service:\s+([a-zA-Z0-9_\-]+)") {
                $serviceName = $matches[1]
            }
        }
        catch {
            Write-Log -Level Warning -Message "Could not read install script content to extract service name."
        }

        # If we can't extract the service name from the script, look for clues in the output
        if (-not $serviceName -and $output -match "service:\s+([a-zA-Z0-9_\-]+)") {
            $serviceName = $matches[1]
        }

        if ($serviceName) {
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if (-not $service) {
                Write-Log -Level Error -Message "Service '$serviceName' is not present after install script execution."
                $hadError = $true
            }
            elseif ($service.Status -ne 'Running') {
                Write-Log -Level Error -Message "Service '$serviceName' is not running after install script execution."
                $hadError = $true
            }
        }

        # Restore original location
        Set-Location -Path $currentLocation

        if ($hadError) {
            if ($output) {
                Write-Log -Level Error -Message "Install output: $output"
            }
            return $false
        }

        Write-Log -Message "Service installed successfully."
        return $true
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation
        Write-Log -Level Error -Message "Failed to install service: $_"
        return $false
    }
}

# Function to copy files from installation folder to service folder
function Copy-InstallationToService {
    [CmdletBinding()]
    param()

    $errors = @()

    try {
        # First check if the installation folder exists and has content
        if (-not (Test-Path -Path $InstallationFolder)) {
            $errors += "Installation folder '$InstallationFolder' does not exist."
            return $errors
        }

        # Get items in the installation folder
        $items = Get-ChildItem -Path $InstallationFolder -Force
        if (-not $items) {
            $errors += "Installation folder '$InstallationFolder' is empty."
            return $errors
        }

        # Create the service folder if it doesn't exist
        if (-not (Test-Path -Path $ServiceFolder)) {
            New-Item -Path $ServiceFolder -ItemType Directory -Force | Out-Null
        }

        # Copy all files from installation folder to service folder
        Write-Log -Message "Copying files from '$InstallationFolder' to '$ServiceFolder'..."
        try {
            # Try bulk copy first
            Copy-Item -Path "$InstallationFolder\*" -Destination $ServiceFolder -Recurse -Force -ErrorAction Stop
            Write-Log -Message "All files copied successfully."
        }
        catch {
            Write-Log -Level Warning -Message "Bulk copy failed, falling back to individual file copy: $_"

            # If bulk copy fails, copy files individually
            $files = Get-ChildItem -Path $InstallationFolder -Recurse

            foreach ($file in $files) {
                try {
                    # Get the relative path from installation folder
                    $relativePath = $file.FullName.Substring($InstallationFolder.Length)
                    $destinationPath = Join-Path -Path $ServiceFolder -ChildPath $relativePath

                    # If it's a directory, create it
                    if ($file.PSIsContainer) {
                        if (-not (Test-Path -Path $destinationPath)) {
                            New-Item -Path $destinationPath -ItemType Directory -Force | Out-Null
                        }
                        Write-Log -Message "Created directory: $destinationPath"
                    }
                    # If it's a file, copy it
                    else {
                        # Ensure the destination directory exists
                        $destinationDir = Split-Path -Path $destinationPath -Parent
                        if (-not (Test-Path -Path $destinationDir)) {
                            New-Item -Path $destinationDir -ItemType Directory -Force | Out-Null
                            Write-Log -Message "Created directory: $destinationDir"
                        }

                        # Copy the file
                        Copy-Item -Path $file.FullName -Destination $destinationPath -Force
                        Write-Log -Message "Copied file: $($file.FullName) to $destinationPath"
                    }
                }
                catch {
                    $errors += "Failed to copy '$($file.FullName)': $_"
                }
            }
        }

        # Verify all files were copied correctly
        $sourceItems = Get-ChildItem -Path $InstallationFolder -Recurse | Where-Object { -not $_.PSIsContainer }
        $destItems = Get-ChildItem -Path $ServiceFolder -Recurse | Where-Object { -not $_.PSIsContainer }

        if ($sourceItems.Count -gt $destItems.Count) {
            $errors += "Not all files were copied. Source count: $($sourceItems.Count), Destination count: $($destItems.Count)"
        }
    }
    catch {
        $errors += "Failed to copy files from installation folder to service folder: $_"
    }

    return $errors
}

# Function to copy files to keep from backup to service folder
function Copy-FilesToKeep {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder
    )

    $errors = @()

    foreach ($file in $FilesToKeep) {
        try {
            $backupFile = Join-Path -Path $BackupFolder -ChildPath $file

            # Check if file exists in backup
            if (Test-Path -Path $backupFile) {
                $destFile = Join-Path -Path $ServiceFolder -ChildPath $file

                # Create directory structure if needed
                $destDir = Split-Path -Path $destFile -Parent
                if (-not (Test-Path -Path $destDir)) {
                    Write-Log -Message "Creating directory: $destDir"
                    New-Item -Path $destDir -ItemType Directory -Force | Out-Null
                }

                # Copy file to service folder
                Copy-Item -Path $backupFile -Destination $destFile -Force -ErrorAction Stop
                Write-Log -Message "Kept file: $file"
            }
            else {
                Write-Log -Level Warning -Message "File to keep not found in backup: $file"
            }
        }
        catch {
            $errors += "Failed to copy file '$file' to keep: $_"
        }
    }

    # Verify that all files to keep were copied
    foreach ($file in $FilesToKeep) {
        $destFile = Join-Path -Path $ServiceFolder -ChildPath $file
        if (-not (Test-Path -Path $destFile)) {
            $errors += "File to keep '$file' was not found in the service folder after copying."
        }
    }

    return $errors
}

# Function to clean up temporary backup folder
function Remove-BackupFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder
    )

    try {
        Write-Log -Message "Cleaning up temporary backup folder: $BackupFolder"

        if (Test-Path -Path $BackupFolder) {
            # Try to delete the backup folder
            Remove-Item -Path $BackupFolder -Recurse -Force -ErrorAction SilentlyContinue

            # Check if deletion was successful
            if (Test-Path -Path $BackupFolder) {
                Write-Log -Level Warning -Message "Could not completely remove backup folder. Some temporary files may remain in: $BackupFolder"
            }
            else {
                Write-Log -Message "Backup folder cleaned up successfully."
            }
        }
        else {
            Write-Log -Level Warning -Message "Backup folder does not exist: $BackupFolder"
        }

        # Return success regardless of whether cleanup succeeded
        return $true
    }
    catch {
        Write-Log -Level Warning -Message "Error cleaning up backup folder: $_"
        # Still return success as we don't want to fail the script for cleanup issues
        return $true
    }
}

# Function to perform rollback in case of failure
function Invoke-Rollback {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder,

        [Parameter(Mandatory = $true)]
        [string]$FailureReason,

        [Parameter(Mandatory = $false)]
        [bool]$MigrationPerformed = $false
    )

    Write-Log -Level Error -Message "FAILURE: $FailureReason"
    Write-Log -Level Warning -Message "Performing rollback..."

    # If migration was performed and we need to roll back, try to migrate down
    if ($MigrationPerformed -and $PerformMigration) {
        Write-Log -Level Warning -Message "Attempting to roll back database migration..."
        $migrateDownResult = Invoke-DatabaseMigration -Down $true

        if (-not $migrateDownResult) {
            Write-Log -Level Error -Message "Failed to roll back database migration. Manual intervention required."
            Write-Log -Level Error -Message "The system is in an inconsistent state. Please restore the database manually."
            Exit 1
        }

        Write-Log -Level Warning -Message "Database migration rolled back successfully."
    }

    # Restore files from backup
    $restoreErrors = Restore-ServiceFromBackup -BackupFolder $BackupFolder

    if ($restoreErrors) {
        Write-Log -Level Error -Message "Errors during rollback:"
        foreach ($err in $restoreErrors) {
            Write-Log -Level Error -Message " - $err"
        }
    }

    # Try to install the service again
    $installResult = Install-Service

    if (-not $installResult) {
        Write-Log -Level Error -Message "Failed to reinstall service during rollback."
        Exit 1
    }

    Write-Log -Level Warning -Message "Rollback completed. Original service has been restored."
    Exit 1
}

# Main execution block
try {
    Write-Log -Message "Starting Windows service reinstallation..."

    # Resolve paths to absolute paths
    $ServiceFolder = Resolve-Path -Path $ServiceFolder -ErrorAction Stop
    $InstallationFolder = Resolve-Path -Path $InstallationFolder -ErrorAction Stop

    # Check prerequisites
    Write-Log -Message "Checking prerequisites..."
    if (-not (Test-Prerequisites)) {
        Write-Log -Level Error -Message "Prerequisites check failed. Terminating."
        Exit 1
    }

    # Create backup
    Write-Log -Message "Creating service backup..."
    $backupFolder = Backup-Service

    if (-not $backupFolder) {
        Write-Log -Level Error -Message "Failed to create backup. Terminating."
        Exit 1
    }

    # Stop and uninstall service
    Write-Log -Message "Stopping and uninstalling service..."
    $uninstallResult = Uninstall-Service

    if (-not $uninstallResult) {
        Write-Log -Level Error -Message "Failed to uninstall service. Terminating."
        Exit 1
    }

    # Migrate database if PerformMigration is true
    $migrationPerformed = $false
    if ($PerformMigration) {
        Write-Log -Message "Performing database migration..."
        $migrateResult = Invoke-DatabaseMigration

        if (-not $migrateResult) {
            Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to migrate database."
        }

        $migrationPerformed = $true
        Write-Log -Message "Database migration completed successfully."
    }

    # Clear service folder
    Write-Log -Message "Clearing service folder..."
    $clearErrors = Clear-ServiceFolder

    # Filter out "not found" errors since they're not actual failures
    $realErrors = $clearErrors | Where-Object { $_ -notmatch "because it does not exist" }

    if ($realErrors -and $realErrors.Count -gt 0) {
        Write-Log -Level Error -Message "Errors during service folder cleanup:"
        foreach ($err in $realErrors) {
            Write-Log -Level Error -Message " - $err"
        }

        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to clear service folder." -MigrationPerformed $migrationPerformed
    }

    # Double check that service folder is empty
    if (-not (Test-FolderEmpty -FolderPath $ServiceFolder)) {
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Service folder is not empty after cleanup." -MigrationPerformed $migrationPerformed
    }

    # Copy files from installation folder to service folder
    Write-Log -Message "Copying files from installation folder to service folder..."
    $copyErrors = Copy-InstallationToService

    if ($copyErrors) {
        Write-Log -Level Error -Message "Errors during file copy:"
        foreach ($err in $copyErrors) {
            Write-Log -Level Error -Message " - $err"
        }

        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to copy installation files." -MigrationPerformed $migrationPerformed
    }

    # Copy files to keep from backup
    Write-Log -Message "Copying files to keep from backup..."
    $keepErrors = Copy-FilesToKeep -BackupFolder $backupFolder

    if ($keepErrors) {
        Write-Log -Level Error -Message "Errors during copying files to keep:"
        foreach ($err in $keepErrors) {
            Write-Log -Level Error -Message " - $err"
        }

        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to copy files to keep." -MigrationPerformed $migrationPerformed
    }

    # Install service
    Write-Log -Message "Installing service..."
    $installResult = Install-Service

    if (-not $installResult) {
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to install service." -MigrationPerformed $migrationPerformed
    }

    # Success
    Write-Log -Message "Service reinstallation completed successfully!"

    # Clean up backup folder (using $null to discard the return value)
    $null = Remove-BackupFolder -BackupFolder $backupFolder

    Exit 0
}
catch {
    Write-Log -Level Error -Message "Unexpected error: $_"

    # If we have a backup folder, try to restore
    if ($backupFolder -and (Test-Path -Path $backupFolder)) {
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Unexpected error occurred." -MigrationPerformed $migrationPerformed
    }
    else {
        Write-Log -Level Error -Message "No backup available for rollback. Service may be in an inconsistent state."
        Exit 1
    }
}
