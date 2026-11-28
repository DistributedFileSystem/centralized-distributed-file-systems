<#
.SYNOPSIS
    Open or remove Windows Defender Firewall inbound TCP rules for DFS nodes.
.DESCRIPTION
    Idempotently creates, updates, or removes the inbound TCP firewall rule named "DFS"
    for the specified ports (default: 7000, 7001). Requires Administrator privileges.
.EXAMPLE
    .\open-firewall.ps1
    .\open-firewall.ps1 -Ports 7000,7001,7002 -WhatIf
    .\open-firewall.ps1 -Remove
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int[]]$Ports = @(7000, 7001),
    [switch]$Remove,
    [string]$DisplayName = "DFS"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Verify Administrator privileges
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator (RunAsAdministrator)."
    exit 1
}

if (-not $Ports -or $Ports.Count -eq 0) {
    Write-Error "At least one TCP port must be specified in -Ports."
    exit 1
}

foreach ($port in $Ports) {
    if ($port -lt 1 -or $port -gt 65535) {
        Write-Error "Invalid port '$port'. Ports must be between 1 and 65535."
        exit 1
    }
}

$portStrings = $Ports | ForEach-Object { $_.ToString() }
$portListText = $portStrings -join ","

$existingRules = @(Get-NetFirewallRule -DisplayName $DisplayName -Direction Inbound -ErrorAction SilentlyContinue)

if ($Remove) {
    if ($existingRules.Count -eq 0) {
        Write-Host "Firewall rule '$DisplayName' does not exist; nothing to remove."
        exit 0
    }
    if ($PSCmdlet.ShouldProcess("Firewall rule '$DisplayName'", "Remove inbound TCP rule")) {
        $existingRules | Remove-NetFirewallRule
        Write-Host "Removed firewall rule '$DisplayName'."
    }
    exit 0
}

if ($existingRules.Count -gt 0) {
    if ($PSCmdlet.ShouldProcess("Firewall rule '$DisplayName' (TCP $portListText)", "Replace existing inbound TCP rule")) {
        $existingRules | Remove-NetFirewallRule
        New-NetFirewallRule `
            -DisplayName $DisplayName `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $portStrings `
            -Action Allow | Out-Null
        Write-Host "Updated firewall rule '$DisplayName' for inbound TCP ports: $portListText."
    }
}
else {
    if ($PSCmdlet.ShouldProcess("Firewall rule '$DisplayName' (TCP $portListText)", "Create inbound TCP rule")) {
        New-NetFirewallRule `
            -DisplayName $DisplayName `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $portStrings `
            -Action Allow | Out-Null
        Write-Host "Created firewall rule '$DisplayName' for inbound TCP ports: $portListText."
    }
}
