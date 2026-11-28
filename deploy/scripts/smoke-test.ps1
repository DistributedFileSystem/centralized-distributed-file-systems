<#
.SYNOPSIS
    TCP connectivity smoke test for DFS LAN cluster endpoints.
.DESCRIPTION
    Tests TCP reachability for each target (host:port) using System.Net.Sockets.TcpClient
    with a configurable timeout, prints PASS/FAIL with latency in ms, and exits 0 iff all pass.
.EXAMPLE
    .\smoke-test.ps1
    .\smoke-test.ps1 -Targets "192.168.10.11:7000","192.168.10.11:7001" -TimeoutSec 2
#>
[CmdletBinding()]
param(
    [string[]]$Targets = @(
        "192.168.10.11:7000",
        "192.168.10.11:7001",
        "192.168.10.12:7001",
        "192.168.10.13:7001",
        "192.168.10.14:7001",
        "192.168.10.15:7001"
    ),
    [int]$TimeoutSec = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($TimeoutSec -lt 1) {
    Write-Error "Invalid -TimeoutSec '$TimeoutSec'. Must be a positive integer."
    exit 1
}

# Support both string array and comma-separated string input
$normalizedTargets = @()
foreach ($entry in $Targets) {
    foreach ($part in ($entry -split ",")) {
        $trimmed = $part.Trim()
        if ($trimmed) {
            $normalizedTargets += $trimmed
        }
    }
}

if ($normalizedTargets.Count -eq 0) {
    Write-Error "No targets specified."
    exit 1
}

$passed = 0
$failed = 0
$timeoutMs = $TimeoutSec * 1000

foreach ($target in $normalizedTargets) {
    $sepIdx = $target.LastIndexOf(":")
    if ($sepIdx -le 0 -or $sepIdx -ge ($target.Length - 1)) {
        Write-Error "Invalid target '$target' (expected host:port)."
        exit 1
    }
    $targetHost = $target.Substring(0, $sepIdx)
    $portStr = $target.Substring($sepIdx + 1)
    [int]$port = 0
    if (-not [int]::TryParse($portStr, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
        Write-Error "Invalid port in target '$target' (must be 1-65535)."
        exit 1
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $client = New-Object System.Net.Sockets.TcpClient
    $async = $null
    $ok = $false
    try {
        $async = $client.BeginConnect($targetHost, $port, $null, $null)
        if ($async.AsyncWaitHandle.WaitOne($timeoutMs, $false)) {
            $client.EndConnect($async)
            $ok = $client.Connected
        }
    }
    catch {
        $ok = $false
    }
    finally {
        $sw.Stop()
        $client.Close()
    }

    $elapsedMs = $sw.ElapsedMilliseconds
    if ($ok) {
        Write-Host "[PASS] $target ($elapsedMs ms)"
        $passed++
    }
    else {
        Write-Host "[FAIL] $target ($elapsedMs ms)"
        $failed++
    }
}

$total = $normalizedTargets.Count
Write-Host "Summary: $passed/$total endpoints reachable."

if ($failed -gt 0) {
    exit 1
}
exit 0
