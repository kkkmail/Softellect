param([string] $messagingDataVersion = "", [string] $versionNumber = "")

. ./MessagingFunctions.ps1
InstallMessagingService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
