param([string] $messagingDataVersion = "")

. ./WorkerNodeFunctions.ps1
UninstallWorkerNodeService -messagingDataVersion $messagingDataVersion
