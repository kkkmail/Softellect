# Create a script-scope variable to keep track of loaded functions
if (-not (Test-Path variable:script:loadedFunctions)) {
    $script:loadedFunctions = @{}
}

function Import-Functions {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string[]]$ExcludeFunctions = @()
    )

    # Get the script directory
    $scriptDirectory = $PSScriptRoot

    # Get the calling script's name to avoid reloading it
    $callerInfo = (Get-PSCallStack)[1]
    $callerPath = $callerInfo.ScriptName
    $callerName = if ($callerPath) { Split-Path -Leaf $callerPath } else { $null }

    # List of scripts to exclude from loading (this script and optionally the caller)
    $excludeScripts = @("Import-Functions.ps1")
    if ($callerName) {
        $excludeScripts += $callerName
    }

    # Add any explicitly excluded functions
    foreach ($excludeFunction in $ExcludeFunctions) {
        $excludeScripts += "$excludeFunction.ps1"
    }

    # Get all PS1 files in the directory except the excluded ones
    $scriptFiles = Get-ChildItem -Path $scriptDirectory -Filter "*.ps1" |
        Where-Object { $excludeScripts -notcontains $_.Name }

    # Load all script files found (only if not already loaded)
    foreach ($scriptFile in $scriptFiles) {
        $scriptPath = Join-Path -Path $scriptDirectory -ChildPath $scriptFile.Name
        $scriptFullPath = Resolve-Path $scriptPath

        # Load only if not already loaded in this session
        if (-not $script:loadedFunctions.ContainsKey($scriptFullPath)) {
            try {
                . $scriptPath
                $script:loadedFunctions[$scriptFullPath] = $true
                Write-Verbose "Loaded function from: $($scriptFile.Name)"
            }
            catch {
                Write-Warning "Failed to load $($scriptFile.Name): $_"
            }
        }
        else {
            Write-Verbose "Function $($scriptFile.Name) already loaded, skipping"
        }
    }
}
