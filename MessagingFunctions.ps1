. ./Functions.ps1
. ./MessagingVersionInfo.ps1
. ./MessagingServiceName.ps1


function InstallMessagingService([string] $messagingDataVersion = "",  [string] $versionNumber = "")
{
    InstallSvc -serviceName $global:messagingServiceName -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
}

function UninstallMessagingService([string] $messagingDataVersion = "")
{
    UninstallSvc -serviceName $global:messagingServiceName -messagingDataVersion $messagingDataVersion
}

function StartMessagingService([string] $messagingDataVersion = "")
{
    StartSvc -serviceName $global:messagingServiceName -messagingDataVersion $messagingDataVersion
}

function StopMessagingService([string] $messagingDataVersion = "")
{
    StopSvc -serviceName $global:messagingServiceName -messagingDataVersion $messagingDataVersion
}
