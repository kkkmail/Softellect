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

# Function to handle logging
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

# Function to verify prerequisites
function Test-Prerequisites {
    [CmdletBinding()]
    param()

    # Construct the migration folder path
    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder

    # Check if the migration folder exists
    if (-not (Test-Path -Path $migrationFolderPath -PathType Container)) {
        Write-Log -Level Error -Message "Migration folder '$migrationFolderPath' does not exist."
        return $false
    }

    # Check if the migration executable exists
    if (-not [string]::IsNullOrEmpty($ExeName)) {
        $exePath = Join-Path -Path $migrationFolderPath -ChildPath $ExeName
        if (-not (Test-Path -Path $exePath -PathType Leaf)) {
            Write-Log -Level Error -Message "Migration executable '$ExeName' not found in the migration folder."
            return $false
        }
    } else {
        # Look for *Migrations*.exe files
        $migrationExes = Get-ChildItem -Path $migrationFolderPath -Filter "*Migrations*.exe"

        if ($migrationExes.Count -eq 0) {
            Write-Log -Level Error -Message "No files matching '*Migrations*.exe' found in the migration folder."
            return $false
        } elseif ($migrationExes.Count -gt 1) {
            Write-Log -Level Error -Message "Multiple files matching '*Migrations*.exe' found in the migration folder:"
            foreach ($exe in $migrationExes) {
                Write-Log -Level Error -Message " - $($exe.Name)"
            }
            Write-Log -Level Error -Message "Please specify the ExeName parameter to select a specific executable."
            return $false
        }
        # If we got here, exactly one migration exe was found, which is good
    }

    # Check that the migration file exists and verify it.
    $migrationFilePath = Join-Path -Path $migrationFolderPath -ChildPath $MigrationFile
    if (-not (Test-Path -Path $migrationFilePath -PathType Leaf)) {
        Write-Log -Level Error -Message "Migration file '$MigrationFile' not found in the migration folder."
        return $false
    }

    # Verify the migration in the file
    if (-not (Invoke-MigrationVerification)) {
        return $false
    }

    return $true
}

# Function to find the migration executable
function Get-MigrationExecutable {
    [CmdletBinding()]
    param()

    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder

    if (-not [string]::IsNullOrEmpty($ExeName)) {
        return Join-Path -Path $migrationFolderPath -ChildPath $ExeName
    } else {
        $migrationExes = Get-ChildItem -Path $migrationFolderPath -Filter "*Migrations*.exe"
        return $migrationExes[0].FullName
    }
}

# Function to execute migration executable with given command
function Invoke-MigrationExecutable {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$OperationName
    )

    $exePath = Get-MigrationExecutable
    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder

    Write-Log -Message "Starting $($OperationName) with command: $Command"

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to migration folder for proper relative path resolution
        Set-Location -Path $migrationFolderPath

        # Create a process object
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exePath
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $psi.Arguments = $Command

        # Start the process
        $process = [System.Diagnostics.Process]::Start($psi)

        # Capture output
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()

        # Wait for the process to exit
        $process.WaitForExit()

        # Get the exit code
        $exitCode = $process.ExitCode

        # Restore original location
        Set-Location -Path $currentLocation

        # Output the results
        if (-not [string]::IsNullOrEmpty($output)) {
            Write-Log -Message "$($OperationName) output: $output"
        }

        if (-not [string]::IsNullOrEmpty($errorOutput)) {
            Write-Log -Level Error -Message "$($OperationName) error output: $errorOutput"
        }

        # Check the exit code
        if ($exitCode -ne 0) {
            Write-Log -Level Error -Message "$($OperationName) failed with exit code: $exitCode"
            return $false
        } else {
            Write-Log -Message "$($OperationName) completed successfully"
            return $true
        }
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation

        $errorMessage = $_.Exception.Message
        Write-Log -Level Error -Message "Failed to execute $($OperationName): $errorMessage"
        return $false
    }
}

# Function to verify the migration in the file
function Invoke-MigrationVerification {
    [CmdletBinding()]
    param()

    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder
    $migrationFilePath = Join-Path -Path $migrationFolderPath -ChildPath $MigrationFile

    Write-Log -Message "Verifying migration in file: $migrationFilePath"

    # Read the migration name from the file
    $migrationName = Get-Content -Path $migrationFilePath -Raw
    $migrationName = $migrationName.Trim()

    Write-Log -Message "Verifying migration: $migrationName"

    # Execute verification command
    return Invoke-MigrationExecutable -Command "verifyFile:$migrationFilePath" -OperationName "Migration verification"
}

# Function to run the migration executable
function Invoke-Migration {
    [CmdletBinding()]
    param()

    $exePath = Get-MigrationExecutable
    Write-Log -Message "Using migration executable: $exePath"

    # Prepare command based on migration direction
    $command = ""
    $operationName = ""
    if ($Down) {
        $operationName = "Database migration DOWN"
        $command = "downFile:$MigrationFile"
        Write-Log -Message "Running database migration DOWN"
    } else {
        $operationName = "Database migration UP"
        $command = "up"
        Write-Log -Message "Running database migration UP"
    }

    # Execute migration command
    return Invoke-MigrationExecutable -Command $command -OperationName $operationName
}

# Main execution
try {
    Write-Log -Message "Starting database migration process..."

    # Resolve the installation folder path
    $InstallationFolder = Resolve-Path -Path $InstallationFolder -ErrorAction Stop

    # Check prerequisites
    Write-Log -Message "Checking prerequisites..."
    if (-not (Test-Prerequisites)) {
        Write-Log -Level Error -Message "Prerequisites check failed. Terminating."
        Exit 1
    }

    # Run the migration
    $result = Invoke-Migration

    if (-not $result) {
        Write-Log -Level Error -Message "Database migration failed. Terminating."
        Exit 1
    }

    # Success
    Write-Log -Message "Database migration completed successfully!"
    Exit 0
}
catch
{
    Write-Log -Level Error -Message "Unexpected error during migration: $_"
    Exit 1
}
