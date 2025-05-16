[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [bool]$Down = $false
)

.\Migrate-Database.ps1 -InstallationFolder "C:\WorkerNode\Solvers\WorkerNodeService" -Down $Down
