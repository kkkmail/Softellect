# Function to validate input folders and scripts
function Test-ServicePrerequisites {
    [CmdletBinding()]
    param(
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
        [bool]$PerformMigration = $false
    )

    $prerequisites = $true

    # Check if service folder exists
    if (-not (Test-Path -Path $ServiceFolder -PathType Container)) {
        Write-ServiceLog -Level Error -Message "Service folder '$ServiceFolder' does not exist."
        $prerequisites = $false
    }

    # Check if installation folder exists
    if (-not (Test-Path -Path $InstallationFolder -PathType Container)) {
        Write-ServiceLog -Level Error -Message "Installation folder '$InstallationFolder' does not exist."
        $prerequisites = $false
    }

    # Check if install script exists in service folder
    if (-not (Test-Path -Path (Join-Path -Path $ServiceFolder -ChildPath $InstallScriptName))) {
        Write-ServiceLog -Level Error -Message "Install script '$InstallScriptName' not found in service folder."
        $prerequisites = $false
    }

    # Check if uninstall script exists in service folder
    if (-not (Test-Path -Path (Join-Path -Path $ServiceFolder -ChildPath $UninstallScriptName))) {
        Write-ServiceLog -Level Error -Message "Uninstall script '$UninstallScriptName' not found in service folder."
        $prerequisites = $false
    }

    # Check if install script exists in installation folder
    if (-not (Test-Path -Path (Join-Path -Path $InstallationFolder -ChildPath $InstallScriptName))) {
        Write-ServiceLog -Level Error -Message "Install script '$InstallScriptName' not found in installation folder."
        $prerequisites = $false
    }

    # Check if migrate script exists in installation folder when PerformMigration is true
    if ($PerformMigration -and -not (Test-Path -Path (Join-Path -Path $InstallationFolder -ChildPath $MigrateScriptName))) {
        Write-ServiceLog -Level Error -Message "Migrate script '$MigrateScriptName' not found in installation folder."
        $prerequisites = $false
    }

    return $prerequisites
}
