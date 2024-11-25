param([string] $messagingDataVersion = "", [string] $versionNumber = "")

. ./PartitionerFunctions.ps1
InstallPartitionerService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
