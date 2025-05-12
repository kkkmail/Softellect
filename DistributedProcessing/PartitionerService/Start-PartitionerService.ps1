param([string] $messagingDataVersion = "")

$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\PartitionerFunctions.ps1"

StartPartitionerService -messagingDataVersion $messagingDataVersion
