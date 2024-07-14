param([string] $messagingDataVersion = "")

. ./MessagingFunctions.ps1
StopMessagingService -messagingDataVersion $messagingDataVersion
