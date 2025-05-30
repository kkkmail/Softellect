function Clean-All {
    [CmdletBinding()]
    param()

 
   # Get the script directory and load dependencies
   $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"

    Clear-Host
    
    Write-ServiceLog "Terminating known orphaned VB / Rider processes..."
    
    $processNames = @('MSBuild', 'VBCSCompiler', 'w3wp')
    $foundProcesses = $false
    
    foreach ($processName in $processNames) {
        $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue
        if ($processes) {
            $foundProcesses = $true
            Write-ServiceLog "Stopping $processName processes..."
            Stop-Process -InputObject $processes -Force
        }
    }
    
    if (-not $foundProcesses) {
        Write-ServiceLog "No known orphaned processes found!"
    }
    
    Write-ServiceLog "Deleting all bin and obj content..."
    
    try {
        $directories = Get-ChildItem -Path "." -Directory -Recurse -ErrorAction Stop
        
        foreach ($directory in $directories) {
            $binFolder = Join-Path -Path $directory.FullName -ChildPath "bin"
            $objFolder = Join-Path -Path $directory.FullName -ChildPath "obj"
            
            if (Test-Path -Path $binFolder) {
                Write-ServiceLog "Deleting folder: $binFolder"
                Remove-Item -Path $binFolder -Recurse -Force -ErrorAction SilentlyContinue
            }
            
            if (Test-Path -Path $objFolder) {
                Write-ServiceLog "Deleting folder: $objFolder"
                Remove-Item -Path $objFolder -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    catch {
        Write-ServiceLog "Error scanning directories: $($_.Exception.Message)" -Level Error
    }
    
    Write-ServiceLog "Deleting all garbage from user Temp folder..."
    try {
        $tempPath = Join-Path -Path $env:USERPROFILE -ChildPath "AppData\Local\Temp"
        if (Test-Path -Path $tempPath) {
            Remove-Item -Path "$tempPath\*" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-ServiceLog "Error cleaning temp folder: $($_.Exception.Message)" -Level Warning
    }
    
    Write-ServiceLog "Deleting all SQL garbage..."
    $sqlPaths = @(
        'C:\Windows\ServiceProfiles\SSISScaleOutMaster140\AppData\Local\SSIS\ScaleOut\Master',
        'C:\Windows\ServiceProfiles\SSISScaleOutMaster160\AppData\Local\SSIS\ScaleOut\16\Master'
    )
    
    foreach ($sqlPath in $sqlPaths) {
        try {
            if (Test-Path -Path $sqlPath) {
                Remove-Item -Path $sqlPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
            Write-ServiceLog "Error cleaning SQL path $sqlPath : $($_.Exception.Message)" -Level Warning
        }
    }
    
    Write-ServiceLog "Clean-All operation completed."
}
