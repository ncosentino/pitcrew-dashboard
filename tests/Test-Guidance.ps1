#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $root 'scripts' 'guidance' 'Test-GuidanceContract.ps1'
$inventoryScript = Join-Path $root 'scripts' 'guidance' 'Get-ValidationInventory.ps1'
$resolver = Join-Path $root 'scripts' 'guidance' 'Get-ApplicableInstructions.ps1'
$scopeResolver = Join-Path $root 'scripts' 'guidance' 'Resolve-ValidationScope.ps1'
$errors = [System.Collections.Generic.List[string]]::new()
$checks = 0
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'pitcrew-dashboard-guidance-tests-' + [guid]::NewGuid().ToString('N'))

function Add-Check {
    param(
        [object]$Condition,
        [string]$Failure
    )

    $script:checks++
    if (-not [bool]$Condition) {
        $script:errors.Add($Failure)
    }
}

function Add-ThrowsCheck {
    param(
        [scriptblock]$Action,
        [string]$ExpectedMessage,
        [string]$Failure
    )

    $script:checks++
    try {
        & $Action
        $script:errors.Add("$Failure No error was thrown.")
    } catch {
        if ($_.Exception.Message -notmatch $ExpectedMessage) {
            $script:errors.Add(
                "$Failure Expected '$ExpectedMessage', got '$($_.Exception.Message)'.")
        }
    }
}

function Copy-Path {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot,
        [string]$RelativePath
    )

    $source = Join-Path $SourceRoot $RelativePath
    $destination = Join-Path $DestinationRoot $RelativePath
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) `
            -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($SourceRoot, $file.FullName)
        $target = Join-Path $DestinationRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
            -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function New-GuidanceFixture {
    param([string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    foreach ($relative in @(
        '.github/genesis-guidance.schema.json',
        'scripts/guidance',
        'tests/Test-Guidance.ps1'
    )) {
        Copy-Path -SourceRoot $root -DestinationRoot $Path -RelativePath $relative
    }

    $files = [ordered]@{
        'AGENTS.md' = '# Agent Instructions'
        'CLAUDE.md' = '@AGENTS.md'
        'README.md' = '# Fixture'
        '.claude/CLAUDE.md' = @"
<!-- copilot-to-claude-compiler:start -->
Generated rules come from Copilot instructions.
<!-- copilot-to-claude-compiler:end -->
"@
        '.github/copilot-instructions.md' = "# Copilot Instructions`n`nUse AGENTS.md."
        '.github/skills/review-changes/SKILL.md' = @"
---
name: review-changes
description: Review fixture changes.
---

Use Get-ApplicableInstructions.ps1 and Get-ValidationInventory.ps1.
"@
        '.github/skills/impeccable/SKILL.md' = "# Impeccable`n"
        '.impeccable/config.json' = "{`n  `"updateCheck`": false`n}`n"
        '.github/agents/impeccable-asset-producer.agent.md' = "# Agent`n"
        '.github/agents/impeccable-documenter.agent.md' = "# Agent`n"
        '.github/agents/impeccable-finish-reviewer.agent.md' = "# Agent`n"
        '.github/agents/impeccable-manual-edit-applier.agent.md' = "# Agent`n"
        '.github/instructions/genesis/common.instructions.md' = @"
---
applyTo: "README.md"
---

# Common
"@
        '.github/instructions/guidance.instructions.md' = @"
---
applyTo: "AGENTS.md,docs/**"
---

# Guidance
"@
        '.github/instructions/impeccable-workflow.instructions.md' = @"
---
applyTo: "docs/impeccable-design.md"
---

# Impeccable
"@
        '.github/instructions/web-ux-resilience.instructions.md' = @"
---
applyTo: "docs/ux-design.md"
---

# UX
"@
        'docs/README.md' = @"
# Documentation

- [Page](page.md)
- [ADRs](adr/README.md)
- [Impeccable](impeccable-design.md)
- [UX](ux-design.md)
"@
        'docs/page.md' = "# Page`n"
        'docs/impeccable-design.md' = "# Impeccable design workflow`n"
        'docs/ux-design.md' = "# UX and design resilience`n"
        'docs/adr/README.md' = @"
# Architecture decision records

- [ADR-0001](adr-0001-test.md)
"@
        'docs/adr/adr-0001-test.md' = @"
---
title: "ADR-0001: Test"
status: "Accepted"
date: "2026-08-08"
authors: []
tags: ["test"]
supersedes: ""
superseded_by: ""
---

# Test decision
"@
    }
    foreach ($entry in $files.GetEnumerator()) {
        $target = Join-Path $Path $entry.Key.Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
            -Force | Out-Null
        Set-Content -LiteralPath $target -Value $entry.Value -Encoding utf8NoBOM
    }

    foreach ($sourceRelative in @(
        '.github/instructions/genesis/common.instructions.md',
        '.github/instructions/guidance.instructions.md',
        '.github/instructions/impeccable-workflow.instructions.md',
        '.github/instructions/web-ux-resilience.instructions.md'
    )) {
        $subPath = $sourceRelative.Substring('.github/instructions/'.Length)
        $generatedRelative = '.claude/rules/' + (
            $subPath -replace '\.instructions\.md$', '.md')
        $target = Join-Path $Path $generatedRelative.Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
            -Force | Out-Null
        Set-Content -LiteralPath $target -Encoding utf8NoBOM -Value @"
---
# AUTO-GENERATED from $sourceRelative — do not edit
paths:
  - "**/*"
---

# Generated
"@
    }

    $contract = [ordered]@{
        '$schema' = './genesis-guidance.schema.json'
        schemaVersion = 1
        docs = [ordered]@{
            mapPath = 'docs/README.md'
            pages = @(
                [ordered]@{
                    path = 'docs/page.md'
                    title = 'Page'
                    section = 'Fixture'
                    order = 1
                    owner = 'fixture'
                },
                [ordered]@{
                    path = 'docs/adr/README.md'
                    title = 'Architecture decision records'
                    section = 'Fixture'
                    order = 2
                    owner = 'fixture'
                },
                [ordered]@{
                    path = 'docs/adr/adr-0001-test.md'
                    title = 'ADR-0001'
                    section = 'Fixture'
                    order = 3
                    owner = 'fixture'
                },
                [ordered]@{
                    path = 'docs/impeccable-design.md'
                    title = 'Impeccable design workflow'
                    section = 'Fixture'
                    order = 4
                    owner = 'fixture'
                },
                [ordered]@{
                    path = 'docs/ux-design.md'
                    title = 'UX and design resilience'
                    section = 'Fixture'
                    order = 5
                    owner = 'fixture'
                }
            )
        }
        agents = [ordered]@{
            path = 'AGENTS.md'
            maxLines = 60
            maxBytes = 3072
            redirects = [ordered]@{
                claude = 'CLAUDE.md'
                copilot = '.github/copilot-instructions.md'
            }
        }
        instructions = [ordered]@{
            managedRoot = '.github/instructions/genesis'
            individualReviewThreshold = [ordered]@{
                lines = 100
                bytes = 8192
            }
            matchedContext = [ordered]@{
                targetLines = 300
                targetBytes = 16384
                maxLines = 600
                maxBytes = 32768
            }
            representativePaths = @('README.md')
        }
        review = [ordered]@{
            skillPath = '.github/skills/review-changes/SKILL.md'
            instructionResolver = 'scripts/guidance/Get-ApplicableInstructions.ps1'
            validationInventory = 'scripts/guidance/Get-ValidationInventory.ps1'
            validationScope = 'scripts/guidance/Resolve-ValidationScope.ps1'
            contractValidator = 'scripts/guidance/Test-GuidanceContract.ps1'
            contractTest = 'tests/Test-Guidance.ps1'
        }
        generatedMirrors = @(
            [ordered]@{
                path = '.claude/rules'
                command = 'fixture compiler'
            }
        )
        contextExceptions = @()
    }
    $contract | ConvertTo-Json -Depth 20 |
        Set-Content -LiteralPath (
            Join-Path $Path '.github' 'genesis-guidance.json'
        ) -Encoding utf8NoBOM
}

try {
    $result = & $validator -ProjectRoot $root
    Add-Check ($result.instructions -eq $result.generatedMirrors) (
        'Instruction and generated mirror counts differ.')
    Add-Check ($result.instructions -ge 130) (
        'The migrated Genesis and project-owned instruction set is incomplete.')
    Add-Check ($result.docs -eq 44) (
        'The documentation map does not contain the expected 44 maintained pages.')
    Add-Check ($result.adrs -eq 8) (
        'The ADR index does not contain eight accepted records.')

    $productPath = Join-Path $root 'PRODUCT.md'
    $designPath = Join-Path $root 'DESIGN.md'
    $designSidecarPath = Join-Path $root '.impeccable' 'design.json'
    $surfaceBriefRoot = Join-Path $root '.impeccable' 'surfaces'
    Add-Check (Test-Path -LiteralPath $productPath -PathType Leaf) (
        'PRODUCT.md is missing.')
    Add-Check (Test-Path -LiteralPath $designPath -PathType Leaf) (
        'DESIGN.md is missing.')
    Add-Check (Test-Path -LiteralPath $designSidecarPath -PathType Leaf) (
        'The Impeccable design-system sidecar is missing.')
    if (
        (Test-Path -LiteralPath $productPath -PathType Leaf) -and
        (Test-Path -LiteralPath $designPath -PathType Leaf) -and
        (Test-Path -LiteralPath $designSidecarPath -PathType Leaf)
    ) {
        $product = Get-Content -LiteralPath $productPath -Raw
        $design = Get-Content -LiteralPath $designPath -Raw
        $designSidecar = Get-Content -LiteralPath $designSidecarPath -Raw |
            ConvertFrom-Json -Depth 100
        Add-Check ($product -match '<!-- impeccable:product-schema 1 -->') (
            'PRODUCT.md does not declare the current Impeccable product schema.')
        Add-Check ($product -match '(?ms)^## Platform\s+web\s*$') (
            'PRODUCT.md does not declare the web platform.')
        Add-Check (
            @(
                '## Overview',
                '## Colors',
                '## Typography',
                '## Layout',
                '## Elevation & Depth',
                '## Shapes',
                '## Components',
                "## Do's and Don'ts"
            ).Where({ $design.Contains($_) }).Count -eq 8
        ) 'DESIGN.md does not contain every canonical design-system section.'
        Add-Check ($designSidecar.schemaVersion -eq 2) (
            'The Impeccable design-system sidecar does not use schema version 2.')
    }

    $surfaceBriefs = @(
        Get-ChildItem -LiteralPath $surfaceBriefRoot -Filter '*.md' -File
    )
    Add-Check ($surfaceBriefs.Count -eq 5) (
        'The expected five Impeccable surface briefs are not present.')
    Add-Check (
        @(
            $surfaceBriefs |
                Where-Object {
                    (Get-Content -LiteralPath $_.FullName -Raw) -notmatch
                        '(?m)^primary_target:\s*"src/'
                }
        ).Count -eq 0
    ) 'One or more Impeccable surface briefs lack a source target.'

    $inventory = & $inventoryScript -ProjectRoot $root
    Add-Check (
        @(
            $inventory.dotnetProjects |
                Where-Object { $_ -match '^\.copilot-worktrees/' }
        ).Count -eq 0
    ) 'Validation inventory included a nested Copilot worktree.'

    $resolverOutput = @(
        & pwsh -NoProfile -File $resolver -Path README.md docs/README.md 2>&1
    )
    Add-Check ($LASTEXITCODE -eq 0) (
        'The documented multi-path resolver invocation failed.')
    Add-Check (
        ($resolverOutput -join "`n") -match 'README\.md' -and
        ($resolverOutput -join "`n") -match 'docs/README\.md'
    ) 'The multi-path resolver did not return both requested paths.'
    $supportResolverOutput = @(
        & $resolver `
            -Path `
            'src/PitCrew.Support.Protocol/SupportEnvelopeCryptography.cs', `
            'src/PitCrew.Dashboard.Features.Support/SupportCarterModule.cs', `
            'src/PitCrew.Support.Relay.App/Program.cs', `
            'src/PitCrew.Support.Agent.App/SupportAgentRequestProcessor.cs', `
            'src/PitCrew.Support.Broker.App/SupportDiagnosticsBroker.cs', `
            'src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/SupportPage.tsx', `
            'assets/support-plane/support-evidence-policy-v0.10.1.json', `
            'scripts/Install-PitCrewSupportPlane.ps1', `
            'tests/Test-SupportPlaneInstaller.Structural.ps1', `
            '.github/workflows/ci.yml' `
            2>&1
    )
    Add-Check ($LASTEXITCODE -eq 0) (
        'The support-plane representative resolver invocation failed.')
    Add-Check (
        @(
            $supportResolverOutput |
                Where-Object {
                    $_.InstructionPath -eq
                        '.github/instructions/support-plane.instructions.md'
                }
        ).Count -ge 10
    ) 'Support-plane representative paths do not resolve support-plane.instructions.md.'
    $supportNegativeOutput = @(
        & $resolver `
            -Path 'src/PitCrew.Connector.Features.Sync/SyncConnectorUnitOfWork.cs' `
            2>&1
    )
    Add-Check (
        @(
            $supportNegativeOutput |
                Where-Object {
                    $_.InstructionPath -eq
                        '.github/instructions/support-plane.instructions.md'
                }
        ).Count -eq 0
    ) 'Support-plane guidance applies to the existing connector synchronization path.'
    $supportInstruction = Get-Content -LiteralPath (
        Join-Path $root '.github' 'instructions' 'support-plane.instructions.md'
    ) -Raw
    Add-Check (
        $supportInstruction -match 'arbitrary paths' -and
        $supportInstruction -match 'tunnels' -and
        $supportInstruction -match 'Docker\s+access' -and
        $supportInstruction -match 'SO_PEERCRED' -and
        $supportInstruction -match 'PrivateNetwork=true' -and
        $supportInstruction -match 'atomic-replacement ACL drift' -and
        $supportInstruction -match 'persisted CNG keys' -and
        $supportInstruction -match '0700' -and
        $supportInstruction -match '0600' -and
        $supportInstruction -match 'never silently re-enroll' -and
        $supportInstruction -match 'preserve-keys or delete-keys' -and
        $supportInstruction -notmatch 'PipeOptions\.CurrentUserOnly'
    ) 'Support-plane guidance does not contain the required v1 negative boundary rules.'
    Add-Check (
        (& $scopeResolver `
            -EventName workflow_dispatch `
            -RequestedScope subset) -ceq 'subset'
    ) 'Workflow-dispatch subset scope does not match CI.'
    Add-Check (
        (& $scopeResolver `
            -EventName pull_request `
            -IsDraft $true `
            -DraftMode subset `
            -ChangedFiles docs/README.md) -ceq 'subset'
    ) 'Draft subset scope does not match the delivery contract.'
    Add-Check (
        (& $scopeResolver `
            -EventName pull_request `
            -IsDraft $false `
            -DraftMode subset) -ceq 'full'
    ) 'Ready pull requests do not resolve to full validation.'
    Add-Check (
        (& $scopeResolver `
            -EventName pull_request `
            -IsDraft $false `
            -ChangedFiles 'docs/README.md','AGENTS.md') -ceq 'guidance-only'
    ) 'Guidance-only paths do not resolve to guidance-only scope.'
    Add-Check (
        (& $scopeResolver `
            -EventName push `
            -ChangedFiles 'docs/README.md') -ceq 'guidance-only'
    ) 'Push with guidance-only paths does not resolve to guidance-only.'
    Add-Check (
        (& $scopeResolver `
            -EventName pull_request `
            -ChangedFiles 'docs/README.md','src/App.cs') -ceq 'full'
    ) 'Mixed guidance and runtime paths do not resolve to full.'

    $base = Join-Path $temporaryRoot 'base'
    New-GuidanceFixture -Path $base
    Add-Check (
        (& $validator -ProjectRoot $base).instructions -eq 4
    ) 'The clean guidance fixture did not validate.'

    $rootBudget = Join-Path $temporaryRoot 'root-budget'
    Copy-Item -LiteralPath $base -Destination $rootBudget -Recurse
    Add-Content -LiteralPath (Join-Path $rootBudget 'AGENTS.md') `
        -Value (('excess guidance' + [Environment]::NewLine) * 100)
    Add-ThrowsCheck {
        & $validator -ProjectRoot $rootBudget | Out-Null
    } 'AGENTS\.md exceeds its budget' (
        'The validator accepted an oversized AGENTS.md.')

    $missingApplyTo = Join-Path $temporaryRoot 'missing-apply-to'
    Copy-Item -LiteralPath $base -Destination $missingApplyTo -Recurse
    Set-Content `
        -LiteralPath (
            Join-Path $missingApplyTo '.github' 'instructions' 'broken.instructions.md'
        ) `
        -Value "---`n`n---`n`n# Broken`n`napplyTo: `"README.md`"`n" `
        -Encoding utf8NoBOM
    Add-ThrowsCheck {
        & $validator -ProjectRoot $missingApplyTo | Out-Null
    } 'has no applyTo value' (
        'The validator accepted applyTo metadata outside frontmatter.')
    Add-ThrowsCheck {
        & (
            Join-Path $missingApplyTo `
                'scripts' 'guidance' 'Get-ApplicableInstructions.ps1'
        ) -Path README.md | Out-Null
    } 'has no applyTo value' (
        'The resolver accepted applyTo metadata outside frontmatter.')

    $duplicateApplyTo = Join-Path $temporaryRoot 'duplicate-apply-to'
    Copy-Item -LiteralPath $base -Destination $duplicateApplyTo -Recurse
    Set-Content `
        -LiteralPath (
            Join-Path $duplicateApplyTo '.github' 'instructions' 'duplicate.instructions.md'
        ) `
        -Value (
            "---`napplyTo: `"README.md`"`napplyTo: `"docs/**`"`n---`n`n# Duplicate`n"
        ) `
        -Encoding utf8NoBOM
    Add-ThrowsCheck {
        & $validator -ProjectRoot $duplicateApplyTo | Out-Null
    } 'appears multiple times' (
        'The validator accepted duplicate instruction metadata.')

    $brokenLink = Join-Path $temporaryRoot 'broken-link'
    Copy-Item -LiteralPath $base -Destination $brokenLink -Recurse
    Add-Content -LiteralPath (Join-Path $brokenLink 'docs' 'README.md') `
        -Value '[Missing](missing-page.md)'
    Add-ThrowsCheck {
        & $validator -ProjectRoot $brokenLink | Out-Null
    } 'does not resolve' (
        'The validator accepted a broken relative documentation link.')

    $contextBudget = Join-Path $temporaryRoot 'context-budget'
    Copy-Item -LiteralPath $base -Destination $contextBudget -Recurse
    $largeBody = 1..320 | ForEach-Object { "Rule $_" }
    @(
        '---'
        'applyTo: "README.md"'
        'reviewThresholdReason: "Controlled matched-context negative fixture."'
        '---'
        ''
        '# Oversized Context'
        ''
        $largeBody
    ) | Set-Content `
        -LiteralPath (
            Join-Path $contextBudget '.github' 'instructions' 'oversized.instructions.md'
        ) `
        -Encoding utf8NoBOM
    Add-ThrowsCheck {
        & $validator -ProjectRoot $contextBudget | Out-Null
    } 'exceeds its matched-context limit' (
        'The validator accepted excessive matched instruction context.')

    $unrepresentedContext = Join-Path $temporaryRoot 'unrepresented-context'
    Copy-Item -LiteralPath $base -Destination $unrepresentedContext -Recurse
    $hiddenPath = Join-Path $unrepresentedContext 'hidden' 'Hidden.cs'
    New-Item -ItemType Directory -Path (Split-Path -Parent $hiddenPath) `
        -Force | Out-Null
    Set-Content -LiteralPath $hiddenPath -Value 'internal sealed class Hidden {}' `
        -Encoding utf8NoBOM
    $hiddenInstruction = Join-Path $unrepresentedContext `
        '.github' 'instructions' 'hidden.instructions.md'
    @(
        '---'
        'applyTo: "hidden/**/*.cs"'
        'reviewThresholdReason: "Controlled whole-repository negative fixture."'
        '---'
        ''
        '# Hidden'
        ''
        (1..650 | ForEach-Object { "Rule $_" })
    ) | Set-Content -LiteralPath $hiddenInstruction -Encoding utf8NoBOM
    Add-ThrowsCheck {
        & $validator -ProjectRoot $unrepresentedContext | Out-Null
    } "Path 'hidden/Hidden\.cs' exceeds the hard matched-context ceiling" (
        'The validator checked only declared representative paths.')

    $staleMirror = Join-Path $temporaryRoot 'stale-mirror'
    Copy-Item -LiteralPath $base -Destination $staleMirror -Recurse
    $mirror = Get-ChildItem -LiteralPath (
        Join-Path $staleMirror '.claude' 'rules'
    ) -Recurse -Filter '*.md' -File | Select-Object -First 1
    Remove-Item -LiteralPath $mirror.FullName
    Add-ThrowsCheck {
        & $validator -ProjectRoot $staleMirror | Out-Null
    } 'Generated Claude rule count' (
        'The validator accepted a missing generated Claude rule.')

    $missingImpeccable = Join-Path $temporaryRoot 'missing-impeccable'
    Copy-Item -LiteralPath $base -Destination $missingImpeccable -Recurse
    Remove-Item -LiteralPath (
        Join-Path $missingImpeccable `
            '.github' 'agents' 'impeccable-finish-reviewer.agent.md'
    )
    Add-ThrowsCheck {
        & $validator -ProjectRoot $missingImpeccable | Out-Null
    } 'Impeccable guidance surface' (
        'The validator accepted an incomplete Impeccable payload.')

    $impeccableUpdate = Join-Path $temporaryRoot 'impeccable-update'
    Copy-Item -LiteralPath $base -Destination $impeccableUpdate -Recurse
    Set-Content -LiteralPath (
        Join-Path $impeccableUpdate '.impeccable' 'config.json'
    ) -Value "{`n  `"updateCheck`": true`n}" -Encoding utf8NoBOM
    Add-ThrowsCheck {
        & $validator -ProjectRoot $impeccableUpdate | Out-Null
    } 'independent update checks must remain disabled' (
        'The validator accepted an independent Impeccable updater.')

    $nestedRepository = Join-Path $temporaryRoot 'nested-repository'
    Copy-Item -LiteralPath $base -Destination $nestedRepository -Recurse
    $nestedGitRoot = Join-Path $nestedRepository 'nested-source'
    New-Item -ItemType Directory -Path (
        Join-Path $nestedGitRoot 'src' 'Foreign.Tests'
    ) -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $nestedGitRoot '.git') `
        -Value 'gitdir: elsewhere' -Encoding utf8NoBOM
    Set-Content -LiteralPath (
        Join-Path $nestedGitRoot 'src' 'Foreign.Tests' 'Foreign.Tests.csproj'
    ) -Value '<Project />' -Encoding utf8NoBOM
    $nestedInventory = & $inventoryScript -ProjectRoot $nestedRepository
    Add-Check (
        @(
            $nestedInventory.dotnetProjects |
                Where-Object { $_ -match 'Foreign' }
        ).Count -eq 0
    ) 'Validation inventory included a nested Git repository.'

    # --- Issue #91: PRODUCT/DESIGN authority checks ---
    $frontendInstruction = Join-Path $root '.github' 'instructions' `
        'frontend-architecture.instructions.md'
    $frontendContent = Get-Content -LiteralPath $frontendInstruction -Raw
    Add-Check ($frontendContent -match 'Impeccable context loader') (
        'Frontend instruction does not require Impeccable context before UI edits.')
    Add-Check ($frontendContent -match '(?i)shape.*new surfaces') (
        'Frontend instruction does not require shape for new surfaces.')
    Add-Check ($frontendContent -match 'approved shared primitives') (
        'Frontend instruction does not require approved shared primitives.')
    Add-Check ($frontendContent -match 'all relevant states') (
        'Frontend instruction does not require all relevant states and viewports.')
    Add-Check ($frontendContent -match 'compact section summary[\s\S]*progressive disclosure') (
        'Frontend instruction does not prevent full-length desktop panel stacking on mobile.')
    Add-Check ($frontendContent -match 'raw identifier.*display name') (
        'Frontend instruction does not prohibit raw IDs when display names exist.')
    Add-Check ($frontendContent -match 'new table.*narrow-screen strategy') (
        'Frontend instruction does not require narrow-screen strategy for tables.')
    Add-Check (
        $frontendContent -match 'card/detail[\s\S]*table/comparison[\s\S]*operator preference'
    ) (
        'Frontend instruction does not require complementary detail and comparison views.')
    Add-Check ($frontendContent -match 'consequential action.*confirmation') (
        'Frontend instruction does not require confirmation for consequential actions.')

    # --- Issue #91: review-changes evidence requirements ---
    $reviewSkill = Join-Path $root '.github' 'skills' 'review-changes' 'SKILL.md'
    $reviewContent = Get-Content -LiteralPath $reviewSkill -Raw
    Add-Check ($reviewContent -match 'Affected routes and states') (
        'review-changes does not require affected-route/state evidence.')
    Add-Check ($reviewContent -match 'Browser results') (
        'review-changes does not require browser results reference.')
    Add-Check ($reviewContent -match 'Keyboard and zoom disclosure') (
        'review-changes does not require keyboard/zoom disclosure.')
    Add-Check ($reviewContent -match 'Localization disclosure') (
        'review-changes does not require localization disclosure.')
    Add-Check ($reviewContent -match 'Finish-reviewer output') (
        'review-changes does not require finish-reviewer output.')
    Add-Check ($reviewContent -match 'Generated mirrors') (
        'review-changes does not require generated mirror evidence.')

    # --- Issue #91: delivery contract includes Browser UX ---
    $deliveryPath = Join-Path $root '.github' 'genesis-delivery.json'
    $delivery = Get-Content -LiteralPath $deliveryPath -Raw |
        ConvertFrom-Json -Depth 20
    Add-Check ($delivery.requiredChecks -contains 'Browser UX') (
        'genesis-delivery.json does not include Browser UX as a required check.')
    $browserWorkflow = $delivery.componentWorkflows |
        Where-Object { $_.path -eq '.github/workflows/browser-ux.yml' }
    Add-Check ($null -ne $browserWorkflow) (
        'genesis-delivery.json does not reference the browser-ux workflow.')

} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

if ($errors.Count -gt 0) {
    throw "Guidance tests failed after $checks checks:`n$($errors -join "`n")"
}

Write-Host "Guidance tests passed: $checks checks."
