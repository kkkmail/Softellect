. ./VersionInfo.ps1

function PrintToConsole($s)
{
    Write-Information -MessageData $s -InformationAction Continue
}

function CleanAll()
{
    cls

    echo "Terminating known orphanged VB / Rider processes..."

    $msb = Get-Process -Name "MSBuild" -ea silentlycontinue
    if ($msb) { Stop-Process -InputObject $msb -force }
    Get-Process | Where-Object {$_.HasExited}

    $vbcsc = Get-Process -Name "VBCSCompiler" -ea silentlycontinue
    if ($vbcsc) { Stop-Process -InputObject $vbcsc -force }
    Get-Process | Where-Object {$_.HasExited}

    $w3wp = Get-Process -Name "w3wp" -ea silentlycontinue
    if ($w3wp) { Stop-Process -InputObject $w3wp -force }
    Get-Process | Where-Object {$_.HasExited}

    if ((!$msb) -and (!$vbcsc) -and (!$w3wp)) { echo "No known orphanded processes found!" }


    echo "Deleting all bin and obj content..."
    $paths = "."

    foreach ($path in $paths)
    {
        $directories = Get-ChildItem -Path $path -Directory -Recurse

        foreach ($directory in $directories)
        {
            $binFolder = Join-Path -Path $directory.FullName -ChildPath "bin"
            $objFolder = Join-Path -Path $directory.FullName -ChildPath "obj"

            if (Test-Path -Path $binFolder)
            {
                Write-Output "Deleting folder: $binFolder"
                Remove-Item -Path $binFolder -Recurse -ErrorAction SilentlyContinue -Force
            }

            if (Test-Path -Path $objFolder)
            {
                Write-Output "Deleting folder: $objFolder"
                Remove-Item -Path $objFolder -Recurse -ErrorAction SilentlyContinue -Force
            }
        }
    }

    echo "Deleting all garbage from user Temp folder..."
    Remove-Item -path $env:userprofile\AppData\Local\Temp -recurse -force -ea silentlycontinue

    echo "Deleting all SQL garbage..."
    Remove-Item -path C:\Windows\ServiceProfiles\SSISScaleOutMaster140\AppData\Local\SSIS\ScaleOut\Master -recurse -force -ea silentlycontinue
    Remove-Item -path C:\Windows\ServiceProfiles\SSISScaleOutMaster160\AppData\Local\SSIS\ScaleOut\16\Master -recurse -force -ea silentlycontinue
}

# https://stackoverflow.com/questions/35064964/powershell-script-to-check-if-service-is-started-if-not-then-start-it
function TryStopService([string] $serviceName)
{
    Write-Host "Attempting to stop service: $serviceName..."
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if($service)
    {
        if ($service.Status -ne 'Running')
        {
            Write-Host "    Service: $serviceName is not running."
        }
        else
        {
            Stop-Service -name $serviceName
            Write-Host "    Stopped service: $serviceName."
        }
    }
    else
    {
        Write-Host "    Service: $serviceName is not found."
    }
}

function UninstallService_Old([string] $serviceName)
{
    Write-Host "Attempting to uninstall service: $serviceName..."
    if (Get-Service $serviceName -ErrorAction SilentlyContinue)
    {
        Remove-Service -Name $serviceName
        Write-Host "    Uninstalled service: $serviceName."
    }
    else
    {
        Write-Host "    Service: $serviceName is not found."
    }
}

function Uninstall-Service-ScExe([string] $serviceName)
{
    try {
        $output = & sc.exe delete $serviceName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    Uninstalled service: $serviceName via sc.exe."
            return $true
        }
        else {
            Write-Host "    sc.exe delete failed with exit code: $LASTEXITCODE. Output: $output"
            return $false
        }
    }
    catch {
        Write-Host "    sc.exe delete failed with exception: $_"
        return $false
    }
}

function Uninstall-Service-WMI([string] $serviceName)
{
    try {
        $wmiService = Get-WmiObject -Class Win32_Service -Filter "Name='$serviceName'"
        if ($wmiService) {
            $result = $wmiService.Delete()
            if ($result.ReturnValue -eq 0) {
                Write-Host "    Uninstalled service: $serviceName via WMI."
                return $true
            }
            else {
                Write-Host "    WMI delete failed with return value: $($result.ReturnValue)."
                return $false
            }
        }
        else {
            Write-Host "    WMI service not found."
            return $false
        }
    }
    catch {
        Write-Host "    WMI delete failed with exception: $_"
        return $false
    }
}

function Uninstall-Service-DotNet([string] $serviceName)
{
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
        
        $result = [ServiceUninstaller]::Uninstall($serviceName)
        if ($result) {
            Write-Host "    Uninstalled service: $serviceName via .NET ServiceInstaller."
            return $true
        }
        else {
            Write-Host "    .NET ServiceInstaller failed."
            return $false
        }
    }
    catch {
        Write-Host "    .NET ServiceInstaller failed with exception: $_"
        return $false
    }
}

function UninstallService([string] $serviceName)
{
    Write-Host "Attempting to stop service: $serviceName..."
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    
    if($service)
    {
        if ($service.Status -eq 'Running')
        {
            Write-Host "    Stopping service: $serviceName."
            Stop-Service -Name $serviceName -Force -ErrorAction Stop
            Write-Host "    Stopped service: $serviceName."
        }
        else
        {
            Write-Host "    Service: $serviceName is not running."
        }
        
        Write-Host "Attempting to uninstall service: $serviceName..."
        
        # Check if Remove-Service cmdlet is available (PS 6+)
        if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
            try {
                Remove-Service -Name $serviceName -ErrorAction Stop
                Write-Host "    Uninstalled service: $serviceName via Remove-Service cmdlet."
                return
            }
            catch {
                Write-Host "    Remove-Service cmdlet failed: $_. Trying alternative methods..."
            }
        }
        
        # Try sc.exe method
        if (Uninstall-Service-ScExe $serviceName) {
            return
        }
        
        # Try WMI method
        if (Uninstall-Service-WMI $serviceName) {
            return
        }
        
        # Try .NET ServiceInstaller method
        if (Uninstall-Service-DotNet $serviceName) {
            return
        }
        
        # If all methods fail, throw an error
        throw "Failed to uninstall service: $serviceName. All methods failed."
    }
    else
    {
        Write-Host "    Service: $serviceName is not found."
    }
}

function StartService([string] $serviceName)
{
    Write-Host "Attempting to start service: $serviceName..."
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if($service)
    {
        if ($service.Status -eq 'Running')
        {
            Write-Host "    Service: $serviceName is already running."
            return
        }
    }

    # Trying to start new service.
    Write-Host "    Trying to start new service: $serviceName."
    Start-Service -Name $serviceName

    #Check that service has started.
    Write-Host "    Waiting 5 seconds to give service time to start..."
    Start-Sleep -s 5
    $testService = Get-Service -Name $serviceName

    if ($testService.Status -ne "Running")
    {
        [string] $errMessage = "    Failed to start service: $serviceName"
        Write-Host $errMessage
        Throw $errMessage
    }
    else
    {
        Write-Host "    Started service: $serviceName."
    }
}

# https://stackoverflow.com/questions/14708825/how-to-create-a-windows-service-in-powershell-for-network-service-account
function ReinstallService ([string] $serviceName, [string] $binaryPath, [string] $description = "", [string] $login = "NT AUTHORITY\LOCAL SERVICE", [string] $password = "", [string] $startUpType = "Automatic")
{
    Write-Host "Attempting to reinstall service: $serviceName..."

    #Check Parameters
    if ((Test-Path $binaryPath)-eq $false)
    {
        Write-Host "    BinaryPath to service was not found: $binaryPath."
        Write-Host "    Service was NOT installed."
        return
    }

    if (("Automatic", "Manual", "Disabled") -notcontains $startUpType)
    {
        Write-Host "    Value for startUpType parameter should be (Automatic or Manual or Disabled) but it was $startUpType."
        Write-Host "    Service was NOT installed."
        return
    }

    TryStopService -serviceName $serviceName
    UninstallService -serviceName $serviceName

    # if password is empty, create a dummy one to allow having credentias for system accounts:
    #     NT AUTHORITY\LOCAL SERVICE
    #     NT AUTHORITY\NETWORK SERVICE
    if ($password -eq "")
    {
        $secpassword = (new-object System.Security.SecureString)
    }
    else
    {
        $secpassword = ConvertTo-SecureString $password -AsPlainText -Force
    }

    $mycreds = New-Object System.Management.Automation.PSCredential ($login, $secpassword)

    # Creating Windows Service using all provided parameters.
    Write-Host "Installing service: $serviceName with user name: '$login'..."
    New-Service -name $serviceName -binaryPathName $binaryPath -Description $description -displayName $serviceName -startupType $startUpType -credential $mycreds
    Write-Host "    Installed service: $serviceName."

    # Trying to start new service.
    StartService -serviceName $serviceName
}

function GetValueOrDefault([string] $value, [string] $messagingDataVersion, [string] $defaultValue)
{
    if ($value -eq "")
    {
        $value = $defaultValue
    }

    return $value
}

function GetServiceName ([string] $serviceName, [string] $messagingDataVersion = "")
{
    $messagingDataVersion = GetValueOrDefault -value $messagingDataVersion -defaultValue $global:messagingDataVersion
    return "$serviceName-$messagingDataVersion"
}

function GetBinaryPathName ([string] $serviceName)
{
    [string] $folderName = Get-Location
    return "$folderName\$serviceName.exe"
}

function GetDescription([string] $serviceName, [string] $messagingDataVersion, [string] $versionNumber)
{
    $messagingDataVersion = GetValueOrDefault -value $messagingDataVersion -defaultValue $global:messagingDataVersion
    [string] $description = "$serviceName, version $versionNumber.$messagingDataVersion"
    return $description
}

function InstallSvc([string] $serviceName, [string] $messagingDataVersion = "",  [string] $versionNumber = "", [string] $login = "NT AUTHORITY\LOCAL SERVICE", [string] $password = "")
{
    $versionNumber = GetValueOrDefault -value $versionNumber -defaultValue $global:versionNumber
    [string] $windowsServiceName = GetServiceName -serviceName $serviceName -messagingDataVersion $messagingDataVersion
    [string] $binaryPath = GetBinaryPathName -serviceName $serviceName
    [string] $description = GetDescription -serviceName $serviceName -versionNumber $versionNumber -messagingDataVersion $messagingDataVersion
    ReinstallService -serviceName $windowsServiceName -binaryPath $binaryPath -description $description -login $login -password $password
}

function UninstallSvc([string] $serviceName, [string] $messagingDataVersion = "")
{
    [string] $windowsServiceName = GetServiceName -serviceName $serviceName -messagingDataVersion $messagingDataVersion
    TryStopService -serviceName $windowsServiceName
    UninstallService -serviceName $windowsServiceName
}

function StartSvc([string] $serviceName, [string] $messagingDataVersion = "")
{
    [string] $windowsServiceName = GetServiceName -serviceName $serviceName -messagingDataVersion $messagingDataVersion
    StartService -serviceName $windowsServiceName
}

function StopSvc([string] $serviceName, [string] $messagingDataVersion = "")
{
    [string] $windowsServiceName = GetServiceName -serviceName $serviceName -messagingDataVersion $messagingDataVersion
    TryStopService -serviceName $windowsServiceName
}

# Default to C: drive if no parameter is provided
function GetFolderSizes([string] $drive = "C:\") {
    Write-Host "Processing input folder: $($drive)"

    # Initialize arrays to store folder sizes and inaccessible folders
    $folderSizes = @()
    $inaccessibleFolders = @()

    # Get top-level folders first to prevent immediate termination on inaccessible folders
    $topLevelFolders = Get-ChildItem -Path $drive -Directory -Force -ErrorAction SilentlyContinue

    foreach ($topFolder in $topLevelFolders) {
        Write-Host "Processing top-level folder: $($topFolder.FullName)"

        try {
            # Recursively get all subfolders for the current top-level folder
            $folders = Get-ChildItem -Path $topFolder.FullName -Directory -Recurse -Force -ErrorAction SilentlyContinue
            $folders += $topFolder  # Include the top-level folder itself

            foreach ($folder in $folders) {
                Write-Host "Processing folder: $($folder.FullName)"

                try {
                    # Get the total size of all files in the folder (excluding subfolders)
                    $size = (Get-ChildItem -Path $folder.FullName -File -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum

                    # Add the folder and its size to the array
                    $folderSizes += [PSCustomObject]@{
                        FolderName = $folder.FullName
                        TotalSize  = $size
                    }
                } catch {
                    # Collect inaccessible folders and report error
                    Write-Host "ERROR: Could not access folder: $($folder.FullName)" -ForegroundColor Red
                    $inaccessibleFolders += $folder.FullName
                }
            }
        } catch {
            # Collect inaccessible top-level folders and report error
            Write-Host "ERROR: Could not access top-level folder: $($topFolder.FullName)" -ForegroundColor Red
            $inaccessibleFolders += $topFolder.FullName
        }
    }

    # Sort the results by TotalSize in descending order
    $sortedResults = $folderSizes | Sort-Object -Property TotalSize -Descending

    # Display the table with TotalSize padded and FolderName
    $sortedResults | ForEach-Object {
        "{0,12:N0}  {1}" -f $_.TotalSize, $_.FolderName
    }

    # Report inaccessible folders
    if ($inaccessibleFolders.Count -gt 0) {
        Write-Host "`nInaccessible Folders:" -ForegroundColor Yellow
        $inaccessibleFolders | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    }
}
