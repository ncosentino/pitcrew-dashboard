#Requires -Version 7.0
<#
.SYNOPSIS
    Path-classification tests for Resolve-ValidationScope.ps1.
    Verifies guidance-only, full, and fail-closed behavior.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolver = Join-Path $root 'scripts' 'guidance' 'Resolve-ValidationScope.ps1'
$errors = [System.Collections.Generic.List[string]]::new()
$checks = 0

function Add-Check {
    param([object]$Condition, [string]$Failure)
    $script:checks++
    if (-not [bool]$Condition) { $script:errors.Add($Failure) }
}

# ── guidance-only paths ──

$guidanceOnlyFiles = @(
    @('PRODUCT.md'),
    @('DESIGN.md'),
    @('.impeccable/design.json'),
    @('.impeccable/surfaces/fleet.md'),
    @('docs/README.md'),
    @('docs/adr/adr-0001-test.md'),
    @('docs/impeccable-design.md'),
    @('.github/instructions/genesis/common.instructions.md'),
    @('.claude/rules/generated/common.md'),
    @('AGENTS.md'),
    @('CLAUDE.md'),
    @('.github/copilot-instructions.md'),
    @('.github/agents/impeccable-finish-reviewer.agent.md'),
    @('.github/skills/review-changes/SKILL.md'),
    @('.github/genesis-guidance.json'),
    @('.github/genesis-guidance.schema.json'),
    @('scripts/guidance/Resolve-ValidationScope.ps1'),
    @('tests/Test-Guidance.ps1'),
    @('docs/README.md', 'AGENTS.md', '.impeccable/surfaces/fleet.md')
)

foreach ($files in $guidanceOnlyFiles) {
    $result = & $resolver -EventName pull_request -ChangedFiles $files
    Add-Check ($result -ceq 'guidance-only') (
        "Expected guidance-only for [$($files -join ', ')], got '$result'.")
}

# ── push event with guidance-only paths ──
$pushResult = & $resolver -EventName push -ChangedFiles @('docs/README.md', 'AGENTS.md')
Add-Check ($pushResult -ceq 'guidance-only') (
    "Push with guidance-only paths should resolve to guidance-only, got '$pushResult'.")

# ── full-trigger paths ──

$fullFiles = @(
    @('src/PitCrew.Dashboard.WebApi/Program.cs'),
    @('src/PitCrew.Dashboard.WebApi/ClientApp/package.json'),
    @('src/PitCrew.Dashboard.WebApi/ClientApp/src/App.tsx'),
    @('Directory.Build.props'),
    @('global.json'),
    @('Dockerfile'),
    @('docker-compose.local.yml'),
    @('deploy/caddy/Caddyfile'),
    @('.container/image.json'),
    @('.github/workflows/ci.yml'),
    @('.github/actions/frontend-gate/action.yml'),
    @('.github/genesis-delivery.json'),
    @('.githooks/pre-push'),
    @('scripts/container/Test-ContainerImage.ps1'),
    @('tests/PitCrew.Dashboard.Tests/SomeTest.cs'),
    @('package.json'),
    @('package-lock.json'),
    @('BannedSymbols.txt'),
    @('Start-LocalDashboard.ps1'),
    @('user-features/some.json'),
    @('assets/logo.png')
)

foreach ($files in $fullFiles) {
    $result = & $resolver -EventName pull_request -ChangedFiles $files
    Add-Check ($result -ceq 'full') (
        "Expected full for [$($files -join ', ')], got '$result'.")
}

# ── mixed guidance + runtime => full ──

$mixedResult = & $resolver -EventName pull_request -ChangedFiles @(
    'docs/README.md', 'src/PitCrew.Dashboard.WebApi/Program.cs'
)
Add-Check ($mixedResult -ceq 'full') (
    "Mixed guidance and runtime should resolve to full, got '$mixedResult'.")

# ── unknown paths => conservative full ──

$unknownResult = & $resolver -EventName pull_request -ChangedFiles @('README.md')
Add-Check ($unknownResult -ceq 'full') (
    "Unknown path 'README.md' should resolve to full, got '$unknownResult'.")

$unknownResult2 = & $resolver -EventName pull_request -ChangedFiles @('random/file.txt')
Add-Check ($unknownResult2 -ceq 'full') (
    "Unknown path 'random/file.txt' should resolve to full, got '$unknownResult2'.")

# ── no evidence => full (fail closed) ──

$noEvidence = & $resolver -EventName pull_request -ChangedFiles @()
Add-Check ($noEvidence -ceq 'full') (
    "No changed files should resolve to full, got '$noEvidence'.")

# ── Conservative switch forces full ──

$conservativeResult = & $resolver -EventName pull_request `
    -ChangedFiles @('docs/README.md') -Conservative
Add-Check ($conservativeResult -ceq 'full') (
    "Conservative switch should force full, got '$conservativeResult'.")

# ── Draft still returns draft mode regardless of paths ──

$draftResult = & $resolver -EventName pull_request -IsDraft $true `
    -DraftMode subset -ChangedFiles @('docs/README.md')
Add-Check ($draftResult -ceq 'subset') (
    "Draft PR should return subset regardless of paths, got '$draftResult'.")

# ── workflow_dispatch respects guidance-only ──

$dispatchResult = & $resolver -EventName workflow_dispatch -RequestedScope 'guidance-only'
Add-Check ($dispatchResult -ceq 'guidance-only') (
    "Workflow dispatch guidance-only should pass through, got '$dispatchResult'.")

# ── Report ──

if ($errors.Count -gt 0) {
    Write-Host "`n❌ $($errors.Count) of $checks path-classification checks failed:" `
        -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "✅ $checks path-classification checks passed." -ForegroundColor Green
