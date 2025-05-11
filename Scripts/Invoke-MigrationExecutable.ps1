function Invoke-MigrationExecutable {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$OperationName,
        
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,
        
        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",
        
        [Parameter(Mandatory = $false)]
        [string]$ExeName = ""
    )

    $exePath = Get-MigrationExecutable -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName
    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder

    Write-ServiceLog -Message "Starting $($OperationName) with command: $Command"

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
            Write-ServiceLog -Message "$($OperationName) output: $output"
        }

        if (-not [string]::IsNullOrEmpty($errorOutput)) {
            Write-ServiceLog -Level Error -Message "$($OperationName) error output: $errorOutput"
        }

        # Check the exit code
        if ($exitCode -ne 0) {
            Write-ServiceLog -Level Error -Message "$($OperationName) failed with exit code: $exitCode"
            return $false
        } else {
            Write-ServiceLog -Message "$($OperationName) completed successfully"
            return $true
        }
    }
    catch {
        # Restore original location in case of error
        Set-Location -Path $currentLocation

        $errorMessage = $_.Exception.Message
        Write-ServiceLog -Level Error -Message "Failed to execute $($OperationName): $errorMessage"
        return $false
    }
}
