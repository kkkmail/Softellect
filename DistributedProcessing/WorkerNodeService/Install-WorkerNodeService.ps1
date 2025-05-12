param([string] $messagingDataVersion = "", [string] $versionNumber = "", [string] $login = "NT AUTHORITY\LOCAL SERVICE", [string] $password = "")

# Get the directory of this script
$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\WorkerNodeVersionInfo.ps1"
. "$scriptDirectory\WorkerNodeServiceName.ps1"
. "$scriptDirectory\WorkerNodeFunctions.ps1"

InstallWorkerNodeService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber -login $login -password $password
