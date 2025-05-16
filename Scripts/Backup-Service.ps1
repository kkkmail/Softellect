# Function to create backup of service
function Backup-Service {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder
    )

    try {
        # Create a unique backup folder name
        $backupFolder = Join-Path -Path $env:TEMP -ChildPath "ServiceBackup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Write-ServiceLog -Message "Creating backup in: $backupFolder"

        # Create the backup folder
        New-Item -Path $backupFolder -ItemType Directory -Force | Out-Null

        # Copy all files and folders from service folder to backup
        Copy-Item -Path "$ServiceFolder\*" -Destination $backupFolder -Recurse -Force

        # Verify backup was successful
        $sourceItems = (Get-ChildItem -Path $ServiceFolder -Recurse | Measure-Object).Count
        $backupItems = (Get-ChildItem -Path $backupFolder -Recurse | Measure-Object).Count

        if ($sourceItems -ne $backupItems) {
            Write-ServiceLog -Level Warning -Message "Backup item count mismatch. Source: $sourceItems, Backup: $backupItems"
        }

        Write-ServiceLog -Message "Backup created successfully."
        return $backupFolder
    }
    catch {
        Write-ServiceLog -Level Error -Message "Failed to create backup: $_"
        return $null
    }
}
