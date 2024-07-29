param([string] $messagingDataVersion = "")

. ./MessagingFunctions.ps1
UninstallMessagingService -messagingDataVersion $messagingDataVersion
