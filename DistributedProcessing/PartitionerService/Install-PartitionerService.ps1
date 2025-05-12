param([string] $Login = "NT AUTHORITY\LOCAL SERVICE", [string] $Password = "")

# Get the directory of this script
$scriptDirectory = $PSScriptRoot

. "$scriptDirectory\PartitionerFunctions.ps1"

InstallPartitionerService -Login $Login -Password $Password
