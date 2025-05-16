function Uninstall-ServiceWithDotNet {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    # Get the script directory and load Write-ServiceLog
    $scriptDirectory = $PSScriptRoot
    . "$scriptDirectory\Write-ServiceLog.ps1"

    try {
        Add-Type -TypeDefinition @"
            using System;
            using System.ServiceProcess;
            using System.Configuration.Install;

            public class ServiceUninstaller {
                public static bool Uninstall(string serviceName) {
                    try {
                        ServiceInstaller installer = new ServiceInstaller();
                        installer.ServiceName = serviceName;
                        installer.Context = new InstallContext();
                        installer.Uninstall(null);
                        return true;
                    }
                    catch {
                        return false;
                    }
                }
            }
"@

        $result = [ServiceUninstaller]::Uninstall($ServiceName)
        if ($result) {
            Write-ServiceLog -Message "Uninstalled service: $ServiceName via .NET ServiceInstaller."
            return $true
        }
        else {
            Write-ServiceLog -Message ".NET ServiceInstaller failed." -Level "Warning"
            return $false
        }
    }
    catch {
        Write-ServiceLog -Message ".NET ServiceInstaller failed with exception: $_" -Level "Error"
        return $false
    }
}
