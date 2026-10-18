#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$workflow = Get-Content -LiteralPath (
    Join-Path $root '.github\workflows\connector-regression.yml'
) -Raw
$delivery = Get-Content -LiteralPath (
    Join-Path $root '.github\genesis-delivery.json'
) -Raw | ConvertFrom-Json
$entry = @($delivery.componentWorkflows | Where-Object {
    $_.path -ceq '.github/workflows/connector-regression.yml'
})
if ($entry.Count -ne 1 -or
    $entry[0].roles -notcontains 'draft-optional' -or
    $entry[0].requiredChecks.Count -ne 0) {
    throw 'Connector regression must remain a declared optional workflow.'
}
foreach ($required in @(
    'workflow_dispatch:',
    'types: [labeled, synchronize, reopened]',
    "github.event_name == 'workflow_dispatch' ||",
    'github.event.pull_request.head.repo.full_name == github.repository &&',
    "contains(github.event.pull_request.labels.*.name, 'ci:connector-regression')",
    'runs-on: [self-hosted, linux, x64, general-purpose]',
    'ref: ${{ github.event.pull_request.head.sha || github.sha }}',
    'set -euo pipefail',
    'PitCrew.Connector.Features.Sync.Tests PitCrew.Dashboard.Features.Fleet.Tests',
    './tests/Test-Guidance.ps1'
)) {
    if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) {
        throw "Connector regression workflow is missing required contract: $required"
    }
}
if ($workflow.Contains('pull_request_target:', [StringComparison]::Ordinal) -or
    $workflow.Contains('continue-on-error: true', [StringComparison]::Ordinal)) {
    throw 'Connector regression must not execute untrusted target events or hide failures.'
}
Write-Host 'Connector regression workflow contract passed.'
