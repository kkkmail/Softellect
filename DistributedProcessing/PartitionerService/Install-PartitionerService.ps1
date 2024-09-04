param([string] $messagingDataVersion = "", [string] $versionNumber = "")

. ./WorkerNodeFunctions.ps1
InstallMessagingService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
