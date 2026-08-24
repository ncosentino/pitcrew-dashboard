#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SupportRelayScriptPath,

    [Parameter(Mandatory)]
    [Uri]$DashboardUrl,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,62}$')]
    [string]$TenantId,

    [Parameter(Mandatory)]
    [Guid]$DashboardNodeId,

    [Parameter(Mandatory)]
    [ValidateSet(
        'ConnectorOffline',
        'CapacityMismatch',
        'JobNotAssigned',
        'HostPressure',
        'Full'
    )]
    [string]$DiagnosticMode,

    [Parameter(Mandatory)]
    [string]$PreflightPath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$ResultPath,

    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $SupportRelayScriptPath -PathType Leaf)) {
    throw 'The candidate PitCrew support-relay script is unavailable.'
}
if (Test-Path -LiteralPath $ResultPath) {
    throw 'The canary support-relay result path already exists.'
}

$result = & $SupportRelayScriptPath `
    -DashboardUrl $DashboardUrl `
    -TenantId $TenantId `
    -DashboardNodeId $DashboardNodeId `
    -DiagnosticMode $DiagnosticMode `
    -Profile default `
    -PreflightPath $PreflightPath `
    -OutputDirectory $OutputDirectory `
    -TimeoutSeconds $TimeoutSeconds `
    -ExpiresInSeconds 300

if (-not $result.completed -or $result.status -cne 'completed') {
    throw 'The candidate PitCrew support-relay scenario did not complete.'
}

$temporaryPath = "$ResultPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $result |
        ConvertTo-Json -Depth 20 |
        Set-Content `
            -LiteralPath $temporaryPath `
            -Encoding utf8NoBOM `
            -NoNewline
    [IO.File]::Move($temporaryPath, $ResultPath, $false)
} finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
