param([string] $messagingDataVersion = "", [string] $versionNumber = "")

. ./Functions.ps1
InstallMessagingService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber
