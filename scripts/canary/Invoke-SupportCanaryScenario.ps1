#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [Parameter(Mandatory)]
    [string]$PitCrewSourceRoot,

    [ValidateSet(
        'topology-smoke-v1',
        'support-fresh-enrollment-diagnostic-v1')]
    [string]$Scenario = 'support-fresh-enrollment-diagnostic-v1',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(30, 1800)]
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

$plan = Read-SupportCanaryPlan -RunRoot $RunRoot
Assert-SupportCanaryCommit `
    -SourceRoot $DashboardSourceRoot `
    -ExpectedCommit ([string]$plan.dashboard.commit)
Assert-SupportCanaryCommit `
    -SourceRoot $PitCrewSourceRoot `
    -ExpectedCommit ([string]$plan.pitCrew.commit)
if (-not (Test-Path -LiteralPath (
    Join-Path $RunRoot 'runtime.json'
) -PathType Leaf)) {
    throw 'The canary runtime manifest is unavailable.'
}
$runnerAssembly = Get-SupportCanaryProjectAssembly `
    -DashboardSourceRoot $DashboardSourceRoot `
    -ProjectName 'PitCrew.Support.Canary.Runner' `
    -Configuration $Configuration
$priorConfiguration = [Environment]::GetEnvironmentVariable(
    'PITCREW_CANARY_DOTNET_CONFIGURATION')
try {
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_DOTNET_CONFIGURATION',
        $Configuration)
    Invoke-SupportCanaryNative dotnet @(
        $runnerAssembly,
        'run',
        '--run-root',
        [IO.Path]::GetFullPath($RunRoot),
        '--scenario',
        $Scenario,
        '--dashboard-source-root',
        [IO.Path]::GetFullPath($DashboardSourceRoot),
        '--pitcrew-source-root',
        [IO.Path]::GetFullPath($PitCrewSourceRoot),
        '--timeout-seconds',
        $TimeoutSeconds.ToString(
            [Globalization.CultureInfo]::InvariantCulture)
    )
} finally {
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_DOTNET_CONFIGURATION',
        $priorConfiguration)
}

Get-Content `
    -LiteralPath (Join-Path $RunRoot "evidence/$Scenario.json") `
    -Raw `
    -Encoding utf8 |
    ConvertFrom-Json -Depth 20
