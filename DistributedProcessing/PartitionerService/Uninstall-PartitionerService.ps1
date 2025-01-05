param([string] $messagingDataVersion = "")

. ./PartitionerFunctions.ps1
UninstallPartitionergService -messagingDataVersion $messagingDataVersion
