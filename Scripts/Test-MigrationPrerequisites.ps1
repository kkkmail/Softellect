function Test-MigrationPrerequisites {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallationFolder,

        [Parameter(Mandatory = $false)]
        [string]$SubFolder = "Migrations",

        [Parameter(Mandatory = $false)]
        [string]$ExeName = "",

        [Parameter(Mandatory = $false)]
        [string]$MigrationFile = "Migration.txt"
    )

    # Construct the migration folder path
    $migrationFolderPath = Join-Path -Path $InstallationFolder -ChildPath $SubFolder

    # Check if the migration folder exists
    if (-not (Test-Path -Path $migrationFolderPath -PathType Container)) {
        Write-ServiceLog -Level Error -Message "Migration folder '$migrationFolderPath' does not exist."
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

    # Check that the migration file exists
    $migrationFilePath = Join-Path -Path $migrationFolderPath -ChildPath $MigrationFile
    if (-not (Test-Path -Path $migrationFilePath -PathType Leaf)) {
        Write-ServiceLog -Level Error -Message "Migration file '$MigrationFile' not found in the migration folder."
        return $false
    }

    return $true
}
