$ip = "8.8.8.8"
$port = 443
$timeoutMs = 10000

try {
    $c = New-Object System.Net.Sockets.TcpClient

    $iar = $c.BeginConnect($ip, $port, $null, $null)
    if (-not $iar.AsyncWaitHandle.WaitOne($timeoutMs)) {
        throw "Connect timeout after ${timeoutMs}ms"
    }

    $c.EndConnect($iar)

    "TCP CONNECT OK to ${ip}:${port}"
}
catch {
    "TCP CONNECT FAILED: $($_.Exception.Message)"
}
finally {
    try { $c.Close() } catch {}
}
