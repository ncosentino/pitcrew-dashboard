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

@(
    'PITCREW_ENROLLMENT_CODE=pending'
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
    up -d --build dashboard
if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose failed with exit code $LASTEXITCODE."
}

$dashboardUri = "http://127.0.0.1:$Port"
$deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
do {
    try {
        Invoke-WebRequest -Uri "$dashboardUri/health" -UseBasicParsing | Out-Null
        $healthy = $true
    } catch {
        $healthy = $false
        Start-Sleep -Seconds 2
    }
} while (-not $healthy -and [DateTimeOffset]::UtcNow -lt $deadline)
if (-not $healthy) {
    throw "Pitcrew dashboard did not become healthy at '$dashboardUri'."
}

$webSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$session = Invoke-RestMethod `
    -Uri "$dashboardUri/api/session" `
    -WebSession $webSession
$enrollment = Invoke-RestMethod `
    -Uri "$dashboardUri/api/tenants/local/fleet/v1/enrollment-codes" `
    -Method Post `
    -WebSession $webSession `
    -Headers @{
        'X-PitCrew-Antiforgery' = [string]$session.antiforgeryToken
    } `
    -ContentType 'application/json' `
    -Body (@{
        label = "Local connector: $ServerName"
    } | ConvertTo-Json)

@(
    "PITCREW_ENROLLMENT_CODE=$($enrollment.code)"
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
    up -d --build connector
if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose failed with exit code $LASTEXITCODE."
}

Write-Host "Pitcrew dashboard: http://127.0.0.1:$Port"
