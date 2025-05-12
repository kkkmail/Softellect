param([string] $messagingDataVersion = "")

$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\PartitionerFunctions.ps1"

UninstallPartitionerService -messagingDataVersion $messagingDataVersion
