param([string] $login = "NT AUTHORITY\LOCAL SERVICE", [string] $password = "")

# Get the directory of this script
$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\WorkerNodeFunctions.ps1"

InstallWorkerNodeService -login $login -password $password
