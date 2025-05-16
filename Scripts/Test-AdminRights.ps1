# Checks that we have the necessary permissions to run the script.
function Test-AdminRights {
    [CmdletBinding()]
    param()

    # Check if we have the necessary permissions
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $hasAdminRights = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    # Check if we can access the service control manager
    $canManageServices = $false
    try {
        $null = Get-Service -Name DoesNotExist -ErrorAction Stop
        $canManageServices = $true
    } catch [System.Management.Automation.RuntimeException] {
        if ($_.Exception.Message -like "*Access is denied*") {
            $canManageServices = $false
        } else {
            $canManageServices = $true
        }
    }

    # If we don't have admin rights but can manage services (like LOCAL SERVICE can), continue
    if (-not $hasAdminRights -and $canManageServices) {
        Write-ServiceLog -Message "Running with service management permissions (though not full administrator)"
        return $true
    } elseif ($hasAdminRights) {
        return $true
    } else {
        Write-ServiceLog -Level Error -Message "This script requires administrator privileges or service management permissions to run."
        return $false
    }
}
