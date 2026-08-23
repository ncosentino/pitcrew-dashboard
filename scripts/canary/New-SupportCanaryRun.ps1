#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$DashboardCommit,

    [Parameter(Mandatory)]
    [string]$PitCrewSourceRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$PitCrewCommit,

    [Parameter(Mandatory)]
    [string]$OutputRoot,

    [ValidateSet(
        'topology-smoke-v1',
        'support-fresh-enrollment-diagnostic-v1')]
    [string[]]$Scenario = @(
        'support-fresh-enrollment-diagnostic-v1'),

    [ValidateSet('portable')]
    [string]$TopologyProfile = 'portable'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

Assert-SupportCanaryCommit `
    -SourceRoot $DashboardSourceRoot `
    -ExpectedCommit $DashboardCommit
Assert-SupportCanaryCommit `
    -SourceRoot $PitCrewSourceRoot `
    -ExpectedCommit $PitCrewCommit

$runId = [Guid]::NewGuid().ToString('N')
$output = [IO.Path]::GetFullPath($OutputRoot)
$runRoot = Join-Path $output $runId
if (Test-Path -LiteralPath $runRoot) {
    throw 'The generated canary run root already exists.'
}
$fixtureRoot = Join-Path $runRoot 'fixture/pitcrew'
$evidenceRoot = Join-Path $runRoot 'evidence'
$servicesRoot = Join-Path $runRoot 'services'
$null = New-Item -ItemType Directory -Path $fixtureRoot
$null = New-Item -ItemType Directory -Path $evidenceRoot
$null = New-Item -ItemType Directory -Path $servicesRoot

foreach ($sentinel in @(
        'Setup-Runner.ps1',
        'RunnerProfiles.Functions.ps1',
        'docker-compose.yml'
    )) {
    Copy-Item `
        -LiteralPath (Join-Path $PitCrewSourceRoot $sentinel) `
        -Destination (Join-Path $fixtureRoot $sentinel)
}
$candidateScripts = Join-Path `
    $PitCrewSourceRoot `
    'plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts'
$fixtureScripts = Join-Path `
    $fixtureRoot `
    'plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts'
$null = New-Item -ItemType Directory -Path $fixtureScripts
Copy-Item `
    -Path (Join-Path $candidateScripts '*') `
    -Destination $fixtureScripts `
    -Recurse
$null = New-Item `
    -ItemType Directory `
    -Path (Join-Path (
        Join-Path $fixtureRoot '.pitcrew-state/default'
    ) 'support-evidence')

$plan = [PSCustomObject][ordered]@{
    schemaVersion = 1
    runId = $runId
    topologyProfile = $TopologyProfile
    scenarios = @($Scenario)
    dashboard = [PSCustomObject][ordered]@{
        repository = 'ncosentino/pitcrew-dashboard'
        commit = $DashboardCommit
    }
    pitCrew = [PSCustomObject][ordered]@{
        repository = 'ncosentino/pitcrew'
        commit = $PitCrewCommit
    }
    createdAt = [DateTimeOffset]::UtcNow.ToString('O')
}
Write-SupportCanaryJson `
    -LiteralPath (Join-Path $runRoot 'plan.json') `
    -Value $plan

(Resolve-Path -LiteralPath $runRoot).Path
