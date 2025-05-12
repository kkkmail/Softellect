param([string] $messagingDataVersion = "")

$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\PartitionerFunctions.ps1"

StopPartitionerService -messagingDataVersion $messagingDataVersion
