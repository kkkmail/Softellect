param([string] $messagingDataVersion = "")

. ./PartitionerFunctions.ps1
StartPartitionerService -messagingDataVersion $messagingDataVersion
