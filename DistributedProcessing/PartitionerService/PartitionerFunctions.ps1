. ./Functions.ps1
. ./PartitionerVersionInfo.ps1
. ./PartitionerServiceName.ps1


function InstallPartitionerService([string] $messagingDataVersion = "",  [string] $versionNumber = "")
{
    InstallSvc -serviceName $global:partitionerServiceName -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
}

function UninstallPartitionergService([string] $messagingDataVersion = "")
{
    UninstallSvc -serviceName $global:partitionerServiceName -messagingDataVersion $messagingDataVersion
}

function StartPartitionerService([string] $messagingDataVersion = "")
{
    StartSvc -serviceName $global:partitionerServiceName -messagingDataVersion $messagingDataVersion
}

function StopPartitionerService([string] $messagingDataVersion = "")
{
    StopSvc -serviceName $global:partitionerServiceName -messagingDataVersion $messagingDataVersion
}
