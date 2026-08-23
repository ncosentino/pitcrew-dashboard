#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

$plan = Read-SupportCanaryPlan -RunRoot $RunRoot
Assert-SupportCanaryCommit `
    -SourceRoot $DashboardSourceRoot `
    -ExpectedCommit ([string]$plan.dashboard.commit)
$runtimePath = Join-Path $RunRoot 'runtime.json'
$processPath = Join-Path $RunRoot 'topology-process.json'
if ((Test-Path -LiteralPath $runtimePath) -or
    (Test-Path -LiteralPath $processPath)) {
    throw 'The canary topology already has runtime or process state.'
}
$appHostAssembly = Get-SupportCanaryProjectAssembly `
    -DashboardSourceRoot $DashboardSourceRoot `
    -ProjectName 'PitCrew.Support.Canary.AppHost' `
    -Configuration $Configuration
$secretBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$relaySecret = [Convert]::ToBase64String($secretBytes)
[Security.Cryptography.CryptographicOperations]::ZeroMemory($secretBytes)

$priorEnvironment = @{}
foreach ($name in @(
        'PITCREW_CANARY_RUN_ROOT',
        'PITCREW_CANARY_RUN_ID',
        'PITCREW_CANARY_DASHBOARD_SOURCE_ROOT',
        'PITCREW_CANARY_DOTNET_CONFIGURATION',
        'PITCREW_CANARY_RELAY_SECRET'
    )) {
    $priorEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_RUN_ROOT',
        [IO.Path]::GetFullPath($RunRoot))
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_RUN_ID',
        [string]$plan.runId)
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_DASHBOARD_SOURCE_ROOT',
        [IO.Path]::GetFullPath($DashboardSourceRoot))
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_DOTNET_CONFIGURATION',
        $Configuration)
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_RELAY_SECRET',
        $relaySecret)
    $process = Start-Process `
        -FilePath dotnet `
        -ArgumentList @($appHostAssembly) `
        -WorkingDirectory $RunRoot `
        -RedirectStandardOutput (Join-Path $RunRoot 'topology.stdout.log') `
        -RedirectStandardError (Join-Path $RunRoot 'topology.stderr.log') `
        -PassThru
} finally {
    foreach ($pair in $priorEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            [string]$pair.Key,
            $pair.Value)
    }
    $relaySecret = $null
}

$processState = [PSCustomObject][ordered]@{
    schemaVersion = 1
    runId = [string]$plan.runId
    processId = $process.Id
    startedAt = $process.StartTime.ToUniversalTime().ToString('O')
}
Write-SupportCanaryJson `
    -LiteralPath $processPath `
    -Value $processState

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    if ($process.HasExited) {
        throw 'The canary topology exited before becoming ready.'
    }
    if (Test-Path -LiteralPath $runtimePath -PathType Leaf) {
        return Get-Content `
            -LiteralPath $runtimePath `
            -Raw `
            -Encoding utf8 |
            ConvertFrom-Json -Depth 20
    }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)

throw 'The canary topology did not become ready within its timeout.'
