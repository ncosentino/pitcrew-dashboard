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
