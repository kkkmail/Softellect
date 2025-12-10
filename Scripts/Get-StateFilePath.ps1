# Function to get the full path to the reinstallation state file
function Get-StateFilePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceFolder,

        [Parameter(Mandatory = $false)]
        [bool]$EnsureDirectoryExists = $true
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Get-ServiceName.ps1"

    try {
        # Extract service name from service folder path
        $serviceName = Split-Path -Path $ServiceFolder -Leaf
        $fullServiceName = Get-ServiceName -ServiceName $serviceName

        # Create state file path in a persistent location (ProgramData)
        $stateDirectory = Join-Path -Path $env:ProgramData -ChildPath "ServiceReinstallState"
        $stateFilePath = Join-Path -Path $stateDirectory -ChildPath "$fullServiceName.json"

        # Ensure directory exists if requested
        if ($EnsureDirectoryExists -and -not (Test-Path -Path $stateDirectory)) {
            New-Item -Path $stateDirectory -ItemType Directory -Force | Out-Null
            Write-ServiceLog -Message "Created state directory: $stateDirectory"
        }

        return $stateFilePath
    }
    catch {
        Write-ServiceLog -Level Error -Message "Failed to get state file path: $_"
        return $null
    }
}
