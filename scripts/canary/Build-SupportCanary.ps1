#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [Parameter(Mandatory)]
    [string]$PitCrewSourceRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
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

foreach ($project in @(
        'PitCrew.Support.Canary.AppHost',
        'PitCrew.Support.Agent.App',
        'PitCrew.Support.Broker.App'
    )) {
    $projectPath = Join-Path `
        $DashboardSourceRoot `
        "src/$project/$project.csproj"
    Invoke-SupportCanaryNative dotnet @(
        'build',
        $projectPath,
        '--configuration',
        $Configuration,
        '--nologo'
    )
}

if ([string]$plan.topologyProfile -ceq 'windows-installed') {
    if (-not $IsWindows) {
        throw 'The windows-installed canary profile requires Windows.'
    }
    Invoke-SupportCanaryNative pwsh @(
        '-NoProfile',
        '-File',
        (Join-Path $DashboardSourceRoot 'scripts/package-support-plane.ps1'),
        '-Configuration',
        $Configuration,
        '-Version',
        '0.0.0-canary',
        '-RuntimeIdentifiers',
        'win-x64',
        '-OutputRoot',
        (Join-Path $RunRoot 'artifacts/support-plane')
    )
}
