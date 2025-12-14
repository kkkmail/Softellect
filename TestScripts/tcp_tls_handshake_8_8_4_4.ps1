# tcp_tls_handshake.ps1
$ip = "8.8.4.4"
$port = 443
$sniHost = "dns.google"     # SNI + Host header
$timeoutMs = 10000

$tcp = $null
$ssl = $null

function Fail($msg, $ex=$null) {
    if ($ex) {
        "ERROR: $msg"
        "EX:   $($ex.GetType().FullName): $($ex.Message)"
        if ($ex.InnerException) { "INNER: $($ex.InnerException.GetType().FullName): $($ex.InnerException.Message)" }
    } else {
        "ERROR: $msg"
    }
    exit 1
}

try {
    "TCP connect -> ${ip}:${port}"
    $tcp = New-Object System.Net.Sockets.TcpClient

    $iar = $tcp.BeginConnect($ip, $port, $null, $null)
    if (-not $iar.AsyncWaitHandle.WaitOne($timeoutMs)) { Fail "TCP connect timeout after ${timeoutMs}ms" }
    $tcp.EndConnect($iar)

    $tcp.ReceiveTimeout = $timeoutMs
    $tcp.SendTimeout    = $timeoutMs

    "TCP connected (local $($tcp.Client.LocalEndPoint))"

    $stream = $tcp.GetStream()

    "TLS handshake (SNI=$sniHost)"
    $ssl = New-Object System.Net.Security.SslStream($stream, $false, ([System.Net.Security.RemoteCertificateValidationCallback]{ $true }))
    $ssl.ReadTimeout  = $timeoutMs
    $ssl.WriteTimeout = $timeoutMs

    # Force TLS 1.2 to avoid old defaults on some Windows builds
    $tls12 = [System.Security.Authentication.SslProtocols]::Tls12
    $ssl.AuthenticateAsClient($sniHost, $null, $tls12, $false)

    "TLS OK: $($ssl.SslProtocol)  cipher=$($ssl.CipherAlgorithm) $($ssl.CipherStrength)"

    $req =
        "GET / HTTP/1.1`r`n" +
        "Host: $sniHost`r`n" +
        "User-Agent: tcp_tls_handshake.ps1`r`n" +
        "Connection: close`r`n" +
        "`r`n"

    "HTTP write"
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($req)
    $ssl.Write($bytes, 0, $bytes.Length)
    $ssl.Flush()

    "HTTP read"
    $buf = New-Object byte[] 8192
    $sb = New-Object System.Text.StringBuilder
    while ($true) {
        $n = $ssl.Read($buf, 0, $buf.Length)
        if ($n -le 0) { break }
        [void]$sb.Append([System.Text.Encoding]::ASCII.GetString($buf, 0, $n))
        if ($sb.Length -gt 200000) { break } # safety cap
    }

    $resp = $sb.ToString()
    if ($resp.Length -eq 0) { Fail "No HTTP response bytes received (read returned 0 immediately)" }

    # Print only first few lines to keep it readable
    "RESPONSE (first 20 lines):"
    ($resp -split "`r`n" | Select-Object -First 20) -join "`r`n"

    exit 0
}
catch {
    Fail "Unhandled exception" $_.Exception
}
finally {
    try { if ($ssl) { $ssl.Close() } } catch {}
    try { if ($tcp) { $tcp.Close() } } catch {}
}
