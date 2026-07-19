#Requires -Version 7.0
<#
.SYNOPSIS
    Starts the optional local Pitcrew dashboard and connector.

.PARAMETER PitCrewStateRoot
    Path to the Pitcrew `.pitcrew-state` directory mounted read-only into the connector.

.PARAMETER Port
    Loopback port used to expose the dashboard.

.PARAMETER ServerName
    Display name shown for this connector in the dashboard.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PitCrewStateRoot,

    [ValidateRange(1, 65535)]
    [int]$Port = 5080,

    [string]$ServerName = [Environment]::MachineName
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$resolvedStateRoot = (Resolve-Path -LiteralPath $PitCrewStateRoot).Path
$environmentPath = Join-Path $root '.env.local'
if (-not (Test-Path -LiteralPath $environmentPath)) {
    $tokenBytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($tokenBytes)
    $token = [Convert]::ToBase64String($tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
} else {
    $tokenLine = Get-Content -LiteralPath $environmentPath |
        Where-Object { $_ -match '^PITCREW_ENROLLMENT_TOKEN=' } |
        Select-Object -First 1
    if (-not $tokenLine) {
        throw "Local environment '$environmentPath' does not contain PITCREW_ENROLLMENT_TOKEN."
    }
    $token = $tokenLine.Substring('PITCREW_ENROLLMENT_TOKEN='.Length)
}

@(
    "PITCREW_ENROLLMENT_TOKEN=$token"
    "PITCREW_STATE_ROOT=$resolvedStateRoot"
    "PITCREW_DASHBOARD_PORT=$Port"
    "PITCREW_SERVER_NAME=$ServerName"
) | Set-Content -LiteralPath $environmentPath -Encoding UTF8
if (-not $IsWindows) {
    [IO.File]::SetUnixFileMode(
        $environmentPath,
        [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
}

docker compose `
    --file (Join-Path $root 'docker-compose.local.yml') `
    --env-file $environmentPath `
    up -d --build
if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose failed with exit code $LASTEXITCODE."
}

Write-Host "Pitcrew dashboard: http://127.0.0.1:$Port"
