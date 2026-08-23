#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$WorkspaceRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$DashboardCommit,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$PitCrewCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

$workspace = [IO.Path]::GetFullPath($WorkspaceRoot)
if (Test-Path -LiteralPath $workspace) {
    throw 'The canary source workspace already exists.'
}
$null = New-Item -ItemType Directory -Path $workspace

foreach ($source in @(
        [PSCustomObject]@{
            Name = 'dashboard'
            Url = 'https://github.com/ncosentino/pitcrew-dashboard.git'
            Commit = $DashboardCommit
        },
        [PSCustomObject]@{
            Name = 'pitcrew'
            Url = 'https://github.com/ncosentino/pitcrew.git'
            Commit = $PitCrewCommit
        }
    )) {
    $target = Join-Path $workspace $source.Name
    $null = New-Item -ItemType Directory -Path $target
    Invoke-SupportCanaryNative git @('-C', $target, 'init', '--quiet')
    Invoke-SupportCanaryNative git @(
        '-C', $target, 'remote', 'add', 'origin', $source.Url)
    Invoke-SupportCanaryNative git @(
        '-C', $target, 'fetch', '--quiet', '--depth=1',
        'origin', $source.Commit)
    Invoke-SupportCanaryNative git @(
        '-C', $target, 'checkout', '--quiet', '--detach', 'FETCH_HEAD')
    Assert-SupportCanaryCommit `
        -SourceRoot $target `
        -ExpectedCommit $source.Commit
}

[PSCustomObject][ordered]@{
    dashboardSourceRoot = (Resolve-Path (
        Join-Path $workspace 'dashboard')).Path
    pitCrewSourceRoot = (Resolve-Path (
        Join-Path $workspace 'pitcrew')).Path
}
