#Requires -RunAsAdministrator
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
    [string[]]$FilesToKeep = @("appsettings.json")
)

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
        $result = $true
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

# Function to clear service folder
function Clear-ServiceFolder {
    [CmdletBinding()]
    param()
    
    $errors = @()
    
    try {
        # Get all items in the service folder
        $items = Get-ChildItem -Path $ServiceFolder -Recurse
        
        # Delete each item, collecting errors
        foreach ($item in $items) {
            try {
                Remove-Item -Path $item.FullName -Force -Recurse -ErrorAction Stop
            }
            catch {
                $errors += "Failed to delete '$($item.FullName)': $_"
            }
        }
        
        # Check if folder is empty
        $remainingItems = Get-ChildItem -Path $ServiceFolder -Recurse -ErrorAction SilentlyContinue
        if ($remainingItems) {
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
        }
        
        # Copy all files from backup to service folder
        Copy-Item -Path "$BackupFolder\*" -Destination $ServiceFolder -Recurse -Force -ErrorAction Stop
        
        Write-Log -Message "Service restored from backup successfully."
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
        $result = $true
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
        # Copy all files from installation folder to service folder
        Copy-Item -Path "$InstallationFolder\*" -Destination $ServiceFolder -Recurse -Force -ErrorAction Stop
        Write-Log -Message "Files copied from installation folder to service folder."
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
    
    return $errors
}

# Function to perform rollback in case of failure
function Invoke-Rollback {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupFolder,
        
        [Parameter(Mandatory = $true)]
        [string]$FailureReason
    )
    
    Write-Log -Level Error -Message "FAILURE: $FailureReason"
    Write-Log -Level Warning -Message "Performing rollback..."
    
    # Restore files from backup
    $restoreErrors = Restore-ServiceFromBackup -BackupFolder $BackupFolder
    
    if ($restoreErrors) {
        Write-Log -Level Error -Message "Errors during rollback:"
        foreach ($error in $restoreErrors) {
            Write-Log -Level Error -Message " - $error"
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
    
    # Clear service folder
    Write-Log -Message "Clearing service folder..."
    $clearErrors = Clear-ServiceFolder
    
    if ($clearErrors) {
        Write-Log -Level Error -Message "Errors during service folder cleanup:"
        foreach ($error in $clearErrors) {
            Write-Log -Level Error -Message " - $error"
        }
        
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to clear service folder."
    }
    
    # Double check that service folder is empty
    if (-not (Test-FolderEmpty -FolderPath $ServiceFolder)) {
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Service folder is not empty after cleanup."
    }
    
    # Copy files from installation folder to service folder
    Write-Log -Message "Copying files from installation folder to service folder..."
    $copyErrors = Copy-InstallationToService
    
    if ($copyErrors) {
        Write-Log -Level Error -Message "Errors during file copy:"
        foreach ($error in $copyErrors) {
            Write-Log -Level Error -Message " - $error"
        }
        
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to copy installation files."
    }
    
    # Copy files to keep from backup
    Write-Log -Message "Copying files to keep from backup..."
    $keepErrors = Copy-FilesToKeep -BackupFolder $backupFolder
    
    if ($keepErrors) {
        Write-Log -Level Error -Message "Errors during copying files to keep:"
        foreach ($error in $keepErrors) {
            Write-Log -Level Error -Message " - $error"
        }
        
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to copy files to keep."
    }
    
    # Install service
    Write-Log -Message "Installing service..."
    $installResult = Install-Service
    
    if (-not $installResult) {
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Failed to install service."
    }
    
    # Success
    Write-Log -Message "Service reinstallation completed successfully!"
    Exit 0
}
catch {
    Write-Log -Level Error -Message "Unexpected error: $_"
    
    # If we have a backup folder, try to restore
    if ($backupFolder -and (Test-Path -Path $backupFolder)) {
        Invoke-Rollback -BackupFolder $backupFolder -FailureReason "Unexpected error occurred."
    }
    else {
        Write-Log -Level Error -Message "No backup available for rollback. Service may be in an inconsistent state."
        Exit 1
    }
}
