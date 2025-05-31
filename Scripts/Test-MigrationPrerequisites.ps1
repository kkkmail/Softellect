# Function to verify migration prerequisites
function Test-MigrationPrerequisites {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,

        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,

        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",

        [Parameter(Mandatory = $false)]
        [string]$ExeName = "",

        [Parameter(Mandatory = $false)]
        [string]$MigrationFile = "Migration.txt"
    )

    # Construct the migration folder paths
    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder
    $serviceMigrationFolderPath = Join-Path -Path $ServiceFolder -ChildPath $SubFolder

    # Check if the migration folder exists
    if (-not (Test-Path -Path $migrationFolderPath -PathType Container)) {
        Write-ServiceLog -Level Error -Message "Up migration folder '$migrationFolderPath' does not exist."
        return $false
    }

    if (-not (Test-Path -Path $serviceMigrationFolderPath -PathType Container)) {
        Write-ServiceLog -Level Error -Message "Down migration folder '$serviceMigrationFolderPath' does not exist."
        return $false
    }

    # Check if the migration executable exists
    if (-not [string]::IsNullOrEmpty($ExeName)) {
        $exePath = Join-Path -Path $migrationFolderPath -ChildPath $ExeName
        if (-not (Test-Path -Path $exePath -PathType Leaf)) {
            Write-ServiceLog -Level Error -Message "Migration executable '$ExeName' not found in the migration folder."
            return $false
        }
    } else {
        # Look for *Migrations*.exe files
        $migrationExes = Get-ChildItem -Path $migrationFolderPath -Filter "*Migrations*.exe"

        if ($migrationExes.Count -eq 0) {
            Write-ServiceLog -Level Error -Message "No files matching '*Migrations*.exe' found in the migration folder."
            return $false
        } elseif ($migrationExes.Count -gt 1) {
            Write-ServiceLog -Level Error -Message "Multiple files matching '*Migrations*.exe' found in the migration folder:"
            foreach ($exe in $migrationExes) {
                Write-ServiceLog -Level Error -Message " - $($exe.Name)"
            }
            Write-ServiceLog -Level Error -Message "Please specify the ExeName parameter to select a specific executable."
            return $false
        }
        # If we got here, exactly one migration exe was found, which is good
    }

    # Check that the migration file exists for Up migration (in installation folder)
    $migrationFilePath = Join-Path -Path $migrationFolderPath -ChildPath $MigrationFile
    if (-not (Test-Path -Path $migrationFilePath -PathType Leaf)) {
        Write-ServiceLog -Level Error -Message "Migration file '$MigrationFile' not found in the installation folder."
        return $false
    }

    # Verify the Up migration in the file
    # Note that $MigrationFile is not actually used for the Up migration, as we will apply all available migrations.
    #
    # In addition, the migratioin in the file might not be the top available migration. This depends if the extract command
    # was called before or after the top migration was applied.

    # In any case, this is not important since we don't use it for Up migration. We keep this test as a sanity check
    # to ensure that the migration file is present and can be verified.
    if (-not (Invoke-MigrationVerification -InstallationFolder $InstallationFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile)) {
        return $false
    }

    # Check that the migration file exists for Down migration (in service folder)
    $serviceMigrationFilePath = Join-Path -Path $serviceMigrationFolderPath -ChildPath $MigrationFile
    if (-not (Test-Path -Path $serviceMigrationFilePath -PathType Leaf)) {
        Write-ServiceLog -Level Error -Message "Migration file '$MigrationFile' not found in the service folder."
        return $false
    }

    # Verify the Down migration in the file (using service folder path)
    if (-not (Invoke-MigrationVerification -InstallationFolder $ServiceFolder -SubFolder $SubFolder -ExeName $ExeName -MigrationFile $MigrationFile)) {
        return $false
    }

    return $true
}
