# tcp_test_8_8_8_8_443.ps1
# Minimal TCP+TLS+HTTP test to an IP:443 with an explicit Host header (SNI/Host are different concepts).
# This is for connectivity testing, not correctness of the HTTP service.

$ip   = "8.8.8.8"
$port = 443
$host = "dns.google"   # Host header + SNI name we present in TLS

$connectTimeoutMs = 10000
$readTimeoutMs    = 10000
$writeTimeoutMs   = 10000

try {
    $client = [System.Net.Sockets.TcpClient]::new()
    $iar = $client.BeginConnect($ip, $port, $null, $null)
    if (-not $iar.AsyncWaitHandle.WaitOne($connectTimeoutMs)) {
        throw "Connect timeout after ${connectTimeoutMs}ms"
    }
    $client.EndConnect($iar)

    $client.ReceiveTimeout = $readTimeoutMs
    $client.SendTimeout    = $writeTimeoutMs

    $net = $client.GetStream()

    # TLS stream with permissive cert validation (we only care about connectivity)
    $ssl = [System.Net.Security.SslStream]::new(
        $net,
        $false,
        { param($sender, $cert, $chain, $errors) return $true }
    )

    # TLS handshake (SNI uses $host here)
    $ssl.AuthenticateAsClient($host)

    $writer = [System.IO.StreamWriter]::new($ssl, [System.Text.Encoding]::ASCII)
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true

    $request = @"
GET / HTTP/1.1
Host: $host
User-Agent: SoftellectTcpTest/1.0
Connection: close

"@

    $writer.Write($request)

    $reader = [System.IO.StreamReader]::new($ssl, [System.Text.Encoding]::ASCII)
    $response = $reader.ReadToEnd()

    $response
}
catch {
    "ERROR: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    if ($_.Exception.InnerException) {
        "INNER: $($_.Exception.InnerException.GetType().FullName): $($_.Exception.InnerException.Message)"
    }
}
finally {
    try { if ($reader) { $reader.Dispose() } } catch {}
    try { if ($writer) { $writer.Dispose() } } catch {}
    try { if ($ssl) { $ssl.Dispose() } } catch {}
    try { if ($net) { $net.Dispose() } } catch {}
    try { if ($client) { $client.Close() } } catch {}
}
