param([string] $messagingDataVersion = "")

. ./MessagingFunctions.ps1
StartMessagingService -messagingDataVersion $messagingDataVersion
