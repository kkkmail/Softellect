# We need workerNodeServiceName but messagingDataVersion here.

. ./Functions.ps1
. ./WorkerNodeVersionInfo.ps1
. ./WorkerNodeServiceName.ps1


function InstallWorkerNodeService([string] $messagingDataVersion = "",  [string] $versionNumber = "")
{
    InstallSvc -serviceName $global:workerNodeServiceName -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
}

function UninstallWorkerNodeService([string] $messagingDataVersion = "")
{
    UninstallSvc -serviceName $global:workerNodeServiceName -messagingDataVersion $messagingDataVersion
}

function StartWorkerNodeService([string] $messagingDataVersion = "")
{
    StartSvc -serviceName $global:workerNodeServiceName -messagingDataVersion $messagingDataVersion
}

function StopWorkerNodeService([string] $messagingDataVersion = "")
{
    StopSvc -serviceName $global:workerNodeServiceName -messagingDataVersion $messagingDataVersion
}
