param([string] $messagingDataVersion = "", [string] $versionNumber = "", [string] $login = "NT AUTHORITY\LOCAL SERVICE", [string] $password = "")

. ./WorkerNodeFunctions.ps1
InstallWorkerNodeService -messagingDataVersion $messagingDataVersion -versionNumber $versionNumber -login $login -password $password
