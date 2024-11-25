param([string] $messagingDataVersion = "")

. ./WorkerNodeFunctions.ps1
StopWorkerNodeService -messagingDataVersion $messagingDataVersion
