function Get-BinaryPathName {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    [string]$folderName = Get-Location
    return "$folderName\$ServiceName.exe"
}
