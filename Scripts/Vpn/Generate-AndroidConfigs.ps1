# Generate vpn_config_<USER>.json files from vpn_config.json using per-user GUID + gzipped XML keys.
# Script location: C:\GitHub\Softellect\Scripts\Vpn\
# Keys folder (relative): ..\..\..\!Keys\
# Users file: <keys_folder>\vpn_android_users.json
#
# Expected format of vpn_android_users.json (human-editable):
#
# {
#   "keysPath": "C:\\Full\\Path\\To\\BinaryKeys",
#   "users": {
#     "TEST":  "11111111-2222-3333-4444-555555555555",
#     "ALICE": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
#   }
# }
#
# Rules:
# - key files (<guid>.key and <guid>.pkx) MUST be GZIP streams (header 1F 8B)
# - decompress the GZIP bytes
# - read decompressed bytes as ASCII text
# - remove all whitespace (including CR/LF)
# - write into clientPrivateKey / clientPublicKey
# - also set clientId = <user_guid>
# - overwrite output files without prompting
# - report CREATED vs OVERWROTE with full filenames
# - output JSON is written as ASCII (NOT UTF)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-AllBytes {
    param([Parameter(Mandatory=$true)][string]$path)
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Key file not found: $path"
    }
    [System.IO.File]::ReadAllBytes($path)
}

function Decompress-GZipBytes-Strict {
    param(
        [Parameter(Mandatory=$true)][byte[]]$gzBytes,
        [Parameter(Mandatory=$true)][string]$pathForErrors
    )

    if ($gzBytes.Length -lt 2 -or $gzBytes[0] -ne 0x1F -or $gzBytes[1] -ne 0x8B) {
        throw "Key file is not GZIP (missing 1F 8B header): $pathForErrors"
    }

    $ms = [System.IO.MemoryStream]::new($gzBytes)
    try {
        $gz = [System.IO.Compression.GZipStream]::new($ms, [System.IO.Compression.CompressionMode]::Decompress)
        try {
            $out = [System.IO.MemoryStream]::new()
            try {
                $gz.CopyTo($out)
                $out.ToArray()
            }
            finally { $out.Dispose() }
        }
        finally { $gz.Dispose() }
    }
    finally { $ms.Dispose() }
}

function Read-GzippedAsciiText-Strict {
    param([Parameter(Mandatory=$true)][string]$path)

    $bytes = Read-AllBytes -path $path
    $plainBytes = Decompress-GZipBytes-Strict -gzBytes $bytes -pathForErrors $path
    [System.Text.Encoding]::ASCII.GetString($plainBytes)
}

function To-SingleLineNoWhitespace {
    param([Parameter(Mandatory=$true)][string]$text)
    ($text -replace '\s+', '')
}

$scriptDirectory = $PSScriptRoot
$keysFolder = (Resolve-Path (Join-Path $scriptDirectory "..\..\..\!Keys")).Path

$usersFile    = Join-Path $keysFolder "vpn_android_users.json"
$templateFile = Join-Path $keysFolder "vpn_config.json"

if (-not (Test-Path -LiteralPath $usersFile))    { throw "Users file not found: $usersFile" }
if (-not (Test-Path -LiteralPath $templateFile)) { throw "Template file not found: $templateFile" }

$usersObj = (Get-Content -LiteralPath $usersFile -Raw -Encoding ASCII) | ConvertFrom-Json

if (-not $usersObj.keysPath) { throw "users file missing keysPath" }
if (-not $usersObj.users)    { throw "users file missing users object" }

$binaryKeysPath = $usersObj.keysPath
if (-not (Test-Path -LiteralPath $binaryKeysPath)) {
    throw "keysPath does not exist: $binaryKeysPath"
}

$templateObj = (Get-Content -LiteralPath $templateFile -Raw -Encoding ASCII) | ConvertFrom-Json

$created = 0
$overwritten = 0
$failed = 0

foreach ($p in $usersObj.users.PSObject.Properties) {
    $userName = $p.Name
    $userGuid = $p.Value

    if (-not $userGuid) {
        Write-Host "FAIL: $userName -> empty guid"
        $failed++
        continue
    }

    $outFile = Join-Path $keysFolder ("vpn_config_{0}.json" -f $userName)
    $outFull = Join-Path $keysFolder ("vpn_config_{0}.json" -f $userName)

    try {
        $privPath = Join-Path $binaryKeysPath ("{0}.key" -f $userGuid)
        $pubPath  = Join-Path $binaryKeysPath ("{0}.pkx" -f $userGuid)

        $privXml = Read-GzippedAsciiText-Strict -path $privPath
        $pubXml  = Read-GzippedAsciiText-Strict -path $pubPath

        $cfg = ($templateObj | ConvertTo-Json -Depth 64 | ConvertFrom-Json)

        $cfg.clientId = $userGuid
        $cfg.clientPrivateKey = To-SingleLineNoWhitespace $privXml
        $cfg.clientPublicKey  = To-SingleLineNoWhitespace $pubXml

        $exists = Test-Path -LiteralPath $outFile

        $jsonOut = $cfg | ConvertTo-Json -Depth 64
        [System.IO.File]::WriteAllText($outFile, $jsonOut, [System.Text.Encoding]::ASCII)

        if ($exists) {
            Write-Host "OVERWROTE: $outFull"
            $overwritten++
        } else {
            Write-Host "CREATED:   $outFull"
            $created++
        }
    }
    catch {
        Write-Host "FAIL: $userName -> $($_.Exception.Message)"
        $failed++
    }
}

Write-Host ""
Write-Host "DONE: created=$created, overwritten=$overwritten, failed=$failed"
Write-Host "keysFolder: $keysFolder"
Write-Host "usersFile:  $usersFile"
Write-Host "template:   $templateFile"
Write-Host "keysPath:   $binaryKeysPath"
