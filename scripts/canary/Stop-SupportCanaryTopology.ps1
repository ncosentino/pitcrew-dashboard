#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [ValidateRange(5, 60)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

$plan = Read-SupportCanaryPlan -RunRoot $RunRoot
$processPath = Join-Path $RunRoot 'topology-process.json'
if (-not (Test-Path -LiteralPath $processPath -PathType Leaf)) {
    throw 'The canary topology process record is unavailable.'
}
$state = Get-Content -LiteralPath $processPath -Raw -Encoding utf8 |
    ConvertFrom-Json -Depth 10 -ErrorAction Stop
if ($state.schemaVersion -ne 1 -or
    [string]$state.runId -cne [string]$plan.runId -or
    [int]$state.processId -le 0) {
    throw 'The canary topology process record is invalid.'
}
$process = Get-Process -Id ([int]$state.processId) -ErrorAction Stop
$expectedStart = [DateTimeOffset]::Parse(
    [string]$state.startedAt,
    [Globalization.CultureInfo]::InvariantCulture)
$actualStart = [DateTimeOffset]::new(
    $process.StartTime.ToUniversalTime())
if ([Math]::Abs(
        ($actualStart - $expectedStart).TotalSeconds) -gt 1) {
    throw 'The canary topology process identity no longer matches.'
}
[IO.File]::WriteAllText(
    (Join-Path $RunRoot 'stop.request'),
    [string]$plan.runId,
    [Text.UTF8Encoding]::new($false)
)
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -ErrorAction Stop
    if (-not $process.WaitForExit(10000)) {
        throw 'The exact canary topology process did not stop.'
    }
}
Remove-Item -LiteralPath $processPath -Force
