param([string] $messagingDataVersion = "")

. ./Functions.ps1
StopMessagingService -messagingDataVersion $messagingDataVersion
