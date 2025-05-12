param([string] $Login = "NT AUTHORITY\LOCAL SERVICE", [string] $Password = "")

# Get the directory of this script
$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\WorkerNodeFunctions.ps1"

InstallWorkerNodeService -Login $Login -Password $Password
