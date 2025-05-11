function Test-FolderEmpty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FolderPath
    )

    $items = Get-ChildItem -Path $FolderPath -Force -ErrorAction SilentlyContinue
    return ($null -eq $items -or $items.Count -eq 0)
}
