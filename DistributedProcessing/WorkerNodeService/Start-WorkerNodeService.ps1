param([string] $messagingDataVersion = "")

. ./WorkerNodeFunctions.ps1
StartWorkerNodeService -messagingDataVersion $messagingDataVersion
