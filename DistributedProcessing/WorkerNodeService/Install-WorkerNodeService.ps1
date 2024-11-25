param([string] $messagingDataVersion = "", [string] $versionNumber = "")

. ./WorkerNodeFunctions.ps1
InstallWorkerNodeService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
