[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$InstallationFolder,

    [Parameter(Mandatory = $false)]
    [string]$SubFolder = "Migration",

    [Parameter(Mandatory = $false)]
    [string]$ExeName = "",

    [Parameter(Mandatory = $false)]
    [bool]$Down = $false
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

# Function to run the migration executable
function Invoke-Migration {
    [CmdletBinding()]
    param()

    $exePath = Get-MigrationExecutable
    Write-Log -Message "Using migration executable: $exePath"

    # Prepare arguments based on migration direction
    $arguments = @()
    if ($Down) {
        Write-Log -Message "Running database migration DOWN"
        # For running migrations down, we'll use '/down' as the parameter
        # This is a common convention, but can be adjusted based on your specific exe
        $arguments = @("/down")
    } else {
        Write-Log -Message "Running database migration UP"
        # For running migrations up, we don't pass any parameters
    }

    try {
        # Save current location
        $currentLocation = Get-Location

        # Change to migration folder for proper relative path resolution
        $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder
        Set-Location -Path $migrationFolderPath

        Write-Log -Message "Starting migration process..."

        # Create a process object
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exePath
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true

        # Add arguments if any
        if ($arguments.Count -gt 0) {
            $psi.Arguments = $arguments -join " "
        }

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

        # Output the migration results
        if (-not [string]::IsNullOrEmpty($output)) {
            Write-Log -Message "Migration output: $output"
        }

        if (-not [string]::IsNullOrEmpty($errorOutput)) {
            Write-Log -Level Error -Message "Migration error output: $errorOutput"
        }

        # Check the exit code
        if ($exitCode -ne 0) {
            Write-Log -Level Error -Message "Migration failed with exit code: $exitCode"
            return $false
        }

        Write-Log -Message "Migration completed successfully with exit code: $exitCode"
        return $true
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation

        Write-Log -Level Error -Message "Failed to execute migration: $_"
        return $false
    }
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
