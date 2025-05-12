param([string] $messagingDataVersion = "")

# Get the directory of this script
$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\WorkerNodeFunctions.ps1"

StopWorkerNodeService -messagingDataVersion $messagingDataVersion
