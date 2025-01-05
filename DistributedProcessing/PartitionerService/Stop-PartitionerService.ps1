param([string] $messagingDataVersion = "")

. ./PartitionerFunctions.ps1
StopPartitionerService -messagingDataVersion $messagingDataVersion
