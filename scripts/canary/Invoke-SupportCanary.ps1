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
        'support-fresh-enrollment-diagnostic-v1',
        'support-diagnostic-mode-matrix-v1',
        'support-relay-restart-recovery-v1',
        'support-request-rejection-matrix-v1',
        'support-terminal-lifecycle-v1')]
    [string]$Scenario = 'support-fresh-enrollment-diagnostic-v1',

    [ValidateSet(
        'portable',
        'containerized',
        'windows-installed',
        'linux-installed')]
    [string]$TopologyProfile = 'portable',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runRoot = & (Join-Path $PSScriptRoot 'New-SupportCanaryRun.ps1') `
    -DashboardSourceRoot $DashboardSourceRoot `
    -DashboardCommit $DashboardCommit `
    -PitCrewSourceRoot $PitCrewSourceRoot `
    -PitCrewCommit $PitCrewCommit `
    -OutputRoot $OutputRoot `
    -Scenario $Scenario `
    -TopologyProfile $TopologyProfile
try {
    & (Join-Path $PSScriptRoot 'Build-SupportCanary.ps1') `
        -RunRoot $runRoot `
        -DashboardSourceRoot $DashboardSourceRoot `
        -PitCrewSourceRoot $PitCrewSourceRoot `
        -Configuration $Configuration
    $null = & (Join-Path $PSScriptRoot 'Start-SupportCanaryTopology.ps1') `
        -RunRoot $runRoot `
        -DashboardSourceRoot $DashboardSourceRoot `
        -Configuration $Configuration
    $scenarioTimeoutSeconds = if (
        $TopologyProfile -in @(
            'windows-installed',
            'linux-installed'
        )
    ) {
        600
    } else {
        300
    }
    & (Join-Path $PSScriptRoot 'Invoke-SupportCanaryScenario.ps1') `
        -RunRoot $runRoot `
        -DashboardSourceRoot $DashboardSourceRoot `
        -PitCrewSourceRoot $PitCrewSourceRoot `
        -Scenario $Scenario `
        -Configuration $Configuration `
        -TimeoutSeconds $scenarioTimeoutSeconds
} finally {
    $hasTopologyProcess = Test-Path `
        -LiteralPath (Join-Path $runRoot 'topology-process.json') `
        -PathType Leaf
    $hasContainerBuild = (
        $TopologyProfile -ceq 'containerized' -and
        (Test-Path `
            -LiteralPath (Join-Path $runRoot 'container-topology.json') `
            -PathType Leaf)
    )
    if ($hasTopologyProcess -or $hasContainerBuild) {
        & (Join-Path $PSScriptRoot 'Stop-SupportCanaryTopology.ps1') `
            -RunRoot $runRoot
    }
}
