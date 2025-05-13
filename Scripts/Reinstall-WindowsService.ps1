# https://stackoverflow.com/questions/14708825/how-to-create-a-windows-service-in-powershell-for-network-service-account
function Reinstall-WindowsService {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$BinaryPath,

        [Parameter(Mandatory = $false)]
        [string]$Description = "",

        [Parameter(Mandatory = $false)]
        [string]$Login = "NT AUTHORITY\LOCAL SERVICE",

        [Parameter(Mandatory = $false)]
        [string]$Password = "",

        [Parameter(Mandatory = $false)]
        [ValidateSet("Automatic", "Manual", "Disabled")]
        [string]$StartupType = "Automatic"
    )

    # Get the script directory and load dependencies
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"
    . "$scriptDirectory\Stop-WindowsService.ps1"
    . "$scriptDirectory\Uninstall-WindowsService.ps1"
    . "$scriptDirectory\Start-WindowsService.ps1"

    Write-ServiceLog -Message "Attempting to reinstall service: $ServiceName..."

    # Check Parameters
    if ((Test-Path $BinaryPath) -eq $false) {
        Write-ServiceLog -Message "BinaryPath to service was not found: $BinaryPath." -Level "Error"
        Write-ServiceLog -Message "Service was NOT installed." -Level "Error"
        return
    }

    if (("Automatic", "Manual", "Disabled") -notcontains $StartupType) {
        Write-ServiceLog -Message "Value for StartupType parameter should be (Automatic or Manual or Disabled) but it was $StartupType." -Level "Error"
        Write-ServiceLog -Message "Service was NOT installed." -Level "Error"
        return
    }

    try {
        # Stop and uninstall the service if it exists
        Stop-WindowsService -ServiceName $ServiceName
        Uninstall-WindowsService -ServiceName $ServiceName
        Write-ServiceLog -Message "Service: $ServiceName successfully stopped and uninstalled."

        # If password is empty, create a dummy one to allow having credentials for system accounts:
        #     NT AUTHORITY\LOCAL SERVICE
        #     NT AUTHORITY\NETWORK SERVICE
        if ($Password -eq "") {
            $secpassword = (New-Object System.Security.SecureString)
        }
        else {
            $secpassword = ConvertTo-SecureString $Password -AsPlainText -Force
        }

        Write-ServiceLog -Message "Password created for service: $ServiceName."

        $mycreds = New-Object System.Management.Automation.PSCredential ($Login, $secpassword)
        Write-ServiceLog -Message "Credentials created for service: $ServiceName."

        # Creating Windows Service using all provided parameters.
        Write-ServiceLog -Message "Installing service: $ServiceName with user name: '$Login'..."
        New-Service -Name $ServiceName -BinaryPathName $BinaryPath -Description $Description -DisplayName $ServiceName -StartupType $StartupType -Credential $mycreds
        Write-ServiceLog -Message "Installed service: $ServiceName."

        # Trying to start new service.
        Start-WindowsService -ServiceName $ServiceName
    }
    catch {
        Write-ServiceLog -Message "Error reinstalling service $(ServiceName): $_" -Level "Error"
        throw $_
    }
}
