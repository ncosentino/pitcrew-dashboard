#Requires -Version 7.0
<#
.SYNOPSIS
    Validate PitCrew Dashboard's layered guidance contract.
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
. (Join-Path $PSScriptRoot 'InstructionGlob.Functions.ps1')

$errors = [System.Collections.Generic.List[string]]::new()
$checks = 0

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

function Get-RelativePath {
    param([string]$Path)
    return [IO.Path]::GetRelativePath($ProjectRoot, $Path).Replace('\', '/')
}

function Get-TextMetric {
    param([string]$RelativePath)

    $path = Join-Path $ProjectRoot $RelativePath.Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return [PSCustomObject]@{
            path = $RelativePath
            exists = $false
            lines = 0
            bytes = 0
            content = ''
        }
    }

    $content = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    return [PSCustomObject]@{
        path = $RelativePath
        exists = $true
        lines = @(Get-Content -LiteralPath $path -Encoding UTF8).Count
        bytes = [Text.Encoding]::UTF8.GetByteCount($content)
        content = $content
    }
}

function Get-FrontmatterContent {
    param([string]$Content)

    $match = [regex]::Match(
        $Content,
        '\A---\r?\n(?<frontmatter>.*?)\r?\n---(?:\r?\n|$)',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        return $null
    }
    return $match.Groups['frontmatter'].Value
}

function Get-FrontmatterValue {
    param(
        [string]$Content,
        [string]$Name
    )

    $frontmatter = Get-FrontmatterContent -Content $Content
    if ($null -eq $frontmatter) {
        return ''
    }
    $matches = [regex]::Matches(
        $frontmatter,
        "(?m)^\s*$([regex]::Escape($Name))\s*:\s*(.+?)\s*$")
    if ($matches.Count -gt 1) {
        throw "Frontmatter field '$Name' appears multiple times."
    }
    if ($matches.Count -eq 0) {
        return ''
    }
    return $matches[0].Groups[1].Value.Trim().Trim('"', "'")
}

function Get-ProjectFiles {
    $excluded = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @(
        '.git',
        '.copilot-worktrees',
        '.next',
        '.nuxt',
        '.output',
        'bin',
        'build',
        'coverage',
        'dist',
        'node_modules',
        'obj',
        'site',
        'target'
    )) {
        [void]$excluded.Add($name)
    }

    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($ProjectRoot)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.PSIsContainer) {
                $isNestedRepository = Test-Path `
                    -LiteralPath (Join-Path $item.FullName '.git')
                if (
                    -not $excluded.Contains($item.Name) -and
                    -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
                    -not $isNestedRepository
                ) {
                    $pending.Push($item.FullName)
                }
            } else {
                $item
            }
        }
    }
}

function Test-CompiledInstructionMatch {
    param(
        [object]$Instruction,
        [string]$RelativePath
    )

    foreach ($regex in @($Instruction.regexes)) {
        if ($RelativePath -cmatch $regex) {
            return $true
        }
    }
    return $false
}

$contractPath = Join-Path $ProjectRoot '.github' 'genesis-guidance.json'
$schemaPath = Join-Path $ProjectRoot '.github' 'genesis-guidance.schema.json'
Add-Check (Test-Path -LiteralPath $contractPath -PathType Leaf) (
    'Guidance contract is missing.')
Add-Check (Test-Path -LiteralPath $schemaPath -PathType Leaf) (
    'Guidance schema is missing.')
if ($errors.Count -gt 0) {
    throw "Guidance contract validation failed:`n$($errors -join "`n")"
}

$contractRaw = Get-Content -LiteralPath $contractPath -Raw -Encoding UTF8
try {
    Add-Check (
        $contractRaw | Test-Json -SchemaFile $schemaPath -ErrorAction Stop
    ) 'Guidance contract does not conform to its schema.'
} catch {
    $errors.Add("Guidance schema validation failed: $($_.Exception.Message)")
}
$contract = $contractRaw | ConvertFrom-Json -Depth 30

$agents = Get-TextMetric ([string]$contract.agents.path)
Add-Check $agents.exists 'AGENTS.md is missing.'
Add-Check (
    $agents.lines -le [int]$contract.agents.maxLines -and
    $agents.bytes -le [int]$contract.agents.maxBytes
) "AGENTS.md exceeds its budget: $($agents.lines) lines/$($agents.bytes) bytes."

$claude = Get-TextMetric ([string]$contract.agents.redirects.claude)
Add-Check (
    $claude.exists -and
    $claude.lines -eq 1 -and
    $claude.content.TrimEnd("`r", "`n") -ceq '@AGENTS.md'
) 'CLAUDE.md must be the one-line @AGENTS.md redirect.'

$copilot = Get-TextMetric ([string]$contract.agents.redirects.copilot)
Add-Check (
    $copilot.exists -and
    $copilot.lines -le 3 -and
    $copilot.bytes -le 128 -and
    $copilot.content -match 'AGENTS\.md'
) 'The Copilot root redirect exceeds its budget or does not point to AGENTS.md.'

$claudeProject = Get-TextMetric '.claude/CLAUDE.md'
Add-Check (
    $claudeProject.exists -and
    $claudeProject.content -match 'copilot-to-claude-compiler:start' -and
    $claudeProject.content -match 'copilot-to-claude-compiler:end'
) '.claude/CLAUDE.md does not declare generated-rule ownership.'

$docsRoot = Join-Path $ProjectRoot 'docs'
$mapPath = Join-Path $ProjectRoot ([string]$contract.docs.mapPath)
$allDocs = @(
    Get-ChildItem -LiteralPath $docsRoot -Recurse -Filter '*.md' -File |
        Sort-Object FullName
)
Add-Check (Test-Path -LiteralPath $mapPath -PathType Leaf) (
    "Documentation map '$($contract.docs.mapPath)' is missing.")

$contractPages = @($contract.docs.pages | ForEach-Object { [string]$_.path })
Add-Check (
    $contractPages.Count -eq @($contractPages | Sort-Object -Unique).Count
) 'The guidance contract contains duplicate documentation paths.'
$expectedPages = @(
    $allDocs |
        ForEach-Object { Get-RelativePath $_.FullName } |
        Where-Object { $_ -cne [string]$contract.docs.mapPath } |
        Sort-Object -CaseSensitive
)
Add-Check (
    (@($contractPages | Sort-Object -CaseSensitive) -join "`n") -ceq
    ($expectedPages -join "`n")
) 'The guidance contract documentation list does not match maintained Markdown files.'

$visitedDocs = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$pendingDocs = [Collections.Generic.Queue[string]]::new()
if (Test-Path -LiteralPath $mapPath -PathType Leaf) {
    $pendingDocs.Enqueue((Resolve-Path -LiteralPath $mapPath).Path)
}
while ($pendingDocs.Count -gt 0) {
    $current = $pendingDocs.Dequeue()
    if (-not $visitedDocs.Add($current)) {
        continue
    }

    $content = Get-Content -LiteralPath $current -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($content, '\]\((?<target>[^)]+)\)')) {
        $target = ($match.Groups['target'].Value -split '\s+')[0].Trim('<', '>')
        if (
            [string]::IsNullOrWhiteSpace($target) -or
            $target.StartsWith('#') -or
            $target -match '^[a-z][a-z0-9+.-]*:'
        ) {
            continue
        }

        $target = ($target -split '[?#]')[0]
        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }
        $resolved = [IO.Path]::GetFullPath(
            (Join-Path (Split-Path -Parent $current) $target))
        Add-Check (
            $resolved.StartsWith(
                $ProjectRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolved -PathType Leaf)
        ) "Documentation link '$target' from '$(Get-RelativePath $current)' does not resolve."

        if (
            $resolved.StartsWith(
                $docsRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetExtension($resolved).Equals(
                '.md',
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolved -PathType Leaf)
        ) {
            $pendingDocs.Enqueue($resolved)
        }
    }
}
foreach ($doc in $allDocs) {
    Add-Check ($visitedDocs.Contains($doc.FullName)) (
        "Documentation page '$(Get-RelativePath $doc.FullName)' is not reachable from '$($contract.docs.mapPath)'.")
}

$adrRoot = Join-Path $docsRoot 'adr'
$adrIndexPath = Join-Path $adrRoot 'README.md'
Add-Check (Test-Path -LiteralPath $adrIndexPath -PathType Leaf) (
    'ADR index docs/adr/README.md is missing.')
$adrIndexContent = if (Test-Path -LiteralPath $adrIndexPath -PathType Leaf) {
    Get-Content -LiteralPath $adrIndexPath -Raw -Encoding UTF8
} else {
    ''
}
$adrRecords = @(
    Get-ChildItem -LiteralPath $adrRoot -Filter 'adr-*.md' -File |
        Where-Object Name -match '^adr-\d{4}-.+\.md$' |
        Sort-Object Name
)
foreach ($adr in $adrRecords) {
    $content = Get-Content -LiteralPath $adr.FullName -Raw -Encoding UTF8
    $frontmatter = Get-FrontmatterContent -Content $content
    Add-Check ($null -ne $frontmatter) (
        "ADR '$($adr.Name)' has invalid frontmatter.")
    foreach ($field in @(
        'title',
        'status',
        'date',
        'authors',
        'tags',
        'supersedes',
        'superseded_by'
    )) {
        Add-Check (
            $null -ne $frontmatter -and
            $frontmatter -match "(?m)^\s*$([regex]::Escape($field))\s*:"
        ) "ADR '$($adr.Name)' is missing frontmatter field '$field'."
    }
    $status = Get-FrontmatterValue -Content $content -Name 'status'
    Add-Check (
        $status -in @(
            'Proposed',
            'Accepted',
            'Rejected',
            'Superseded',
            'Deprecated'
        )
    ) "ADR '$($adr.Name)' has unsupported status '$status'."
    Add-Check (
        $adrIndexContent -match [regex]::Escape($adr.Name)
    ) "ADR '$($adr.Name)' is missing from the ADR index."
}

$instructionRoot = Join-Path $ProjectRoot '.github' 'instructions'
$managedRoot = Join-Path $ProjectRoot ([string]$contract.instructions.managedRoot)
Add-Check (Test-Path -LiteralPath $managedRoot -PathType Container) (
    'Genesis-managed instruction root is missing.')
$instructionFiles = @(
    Get-ChildItem -LiteralPath $instructionRoot -Recurse `
        -Filter '*.instructions.md' -File |
        Sort-Object FullName
)
$projectFiles = @(
    Get-ProjectFiles |
        ForEach-Object { Get-RelativePath $_.FullName }
)
$instructionRecords = [System.Collections.Generic.List[object]]::new()
foreach ($instruction in $instructionFiles) {
    $content = Get-Content -LiteralPath $instruction.FullName -Raw -Encoding UTF8
    $relative = Get-RelativePath $instruction.FullName
    $frontmatter = Get-FrontmatterContent -Content $content
    Add-Check ($null -ne $frontmatter) (
        "Instruction '$relative' has invalid frontmatter.")
    $applyTo = ''
    $regexes = [System.Collections.Generic.List[string]]::new()
    try {
        $applyTo = Get-FrontmatterValue -Content $content -Name 'applyTo'
    } catch {
        $errors.Add("Instruction '$relative' has invalid metadata: $($_.Exception.Message)")
    }
    Add-Check (-not [string]::IsNullOrWhiteSpace($applyTo)) (
        "Instruction '$relative' has no applyTo value.")
    if (-not [string]::IsNullOrWhiteSpace($applyTo)) {
        try {
            foreach ($pattern in @(
                Split-InstructionGlobPatterns -ApplyTo $applyTo
            )) {
                foreach ($expanded in @(
                    Expand-InstructionGlobPattern -Pattern $pattern
                )) {
                    $regexes.Add(
                        (ConvertTo-InstructionGlobRegex -Pattern $expanded)
                    )
                }
            }
        } catch {
            $errors.Add("Instruction '$relative' has invalid applyTo '$applyTo': $($_.Exception.Message)")
        }
    }

    $lines = @(Get-Content -LiteralPath $instruction.FullName -Encoding UTF8).Count
    $bytes = [Text.Encoding]::UTF8.GetByteCount($content)
    if (
        $lines -gt [int]$contract.instructions.individualReviewThreshold.lines -or
        $bytes -gt [int]$contract.instructions.individualReviewThreshold.bytes
    ) {
        Add-Check (-not [string]::IsNullOrWhiteSpace(
            (Get-FrontmatterValue -Content $content -Name 'reviewThresholdReason')
        )) "Instruction '$relative' exceeds its review threshold without a reason."
    }

    $matchCount = 0
    if (-not [string]::IsNullOrWhiteSpace($applyTo)) {
        $candidateRecord = [PSCustomObject]@{
            regexes = @($regexes)
        }
        foreach ($candidate in $projectFiles) {
            if (
                Test-CompiledInstructionMatch `
                    -Instruction $candidateRecord `
                    -RelativePath $candidate
            ) {
                $matchCount++
            }
        }
    }
    if (-not $relative.StartsWith(
        '.github/instructions/genesis/',
        [StringComparison]::Ordinal
    )) {
        Add-Check ($matchCount -gt 0) (
            "Instruction '$relative' does not match a current repository file.")
    }
    $instructionRecords.Add([PSCustomObject]@{
        path = $relative
        applyTo = $applyTo
        lines = $lines
        bytes = $bytes
        regexes = @($regexes)
    })
}

$contextMetrics = [System.Collections.Generic.List[object]]::new()
foreach ($representativePath in @($contract.instructions.representativePaths)) {
    $candidate = ([string]$representativePath).Replace('\', '/')
    Add-Check (
        -not [string]::IsNullOrWhiteSpace($candidate) -and
        -not ($candidate.Split('/') -contains '..') -and
        (Test-Path -LiteralPath (
            Join-Path $ProjectRoot $candidate.Replace(
                '/',
                [IO.Path]::DirectorySeparatorChar)
        ) -PathType Leaf)
    ) "Representative path '$candidate' is missing or invalid."

}

foreach ($candidate in ($projectFiles | Sort-Object -CaseSensitive -Unique)) {
    $matches = @(
        $instructionRecords |
            Where-Object {
                Test-CompiledInstructionMatch `
                    -Instruction $_ `
                    -RelativePath $candidate
            }
    )
    if ($matches.Count -eq 0) {
        continue
    }
    $lines = [int](($matches | Measure-Object lines -Sum).Sum ?? 0)
    $bytes = [int](($matches | Measure-Object bytes -Sum).Sum ?? 0)
    $exception = @(
        $contract.contextExceptions |
            Where-Object {
                Test-InstructionGlobMatch `
                    -ApplyTo ([string]$_.pattern) `
                    -RelativePath $candidate
            }
    ) | Select-Object -First 1
    $lineLimit = if ($exception) {
        [int]$exception.maxLines
    } else {
        [int]$contract.instructions.matchedContext.targetLines
    }
    $byteLimit = if ($exception) {
        [int]$exception.maxBytes
    } else {
        [int]$contract.instructions.matchedContext.targetBytes
    }
    Add-Check (
        $lines -le [int]$contract.instructions.matchedContext.maxLines -and
        $bytes -le [int]$contract.instructions.matchedContext.maxBytes
    ) "Path '$candidate' exceeds the hard matched-context ceiling."
    Add-Check (
        $lines -le $lineLimit -and
        $bytes -le $byteLimit
    ) (
        "Path '$candidate' exceeds its matched-context limit: " +
        "$lines lines/$bytes bytes.")
    $contextMetrics.Add([PSCustomObject]@{
        path = $candidate
        instructionPaths = @($matches.path)
        lines = $lines
        bytes = $bytes
    })
}

foreach ($property in $contract.review.PSObject.Properties) {
    $relative = [string]$property.Value
    Add-Check (
        Test-Path -LiteralPath (
            Join-Path $ProjectRoot $relative.Replace(
                '/',
                [IO.Path]::DirectorySeparatorChar)
        ) -PathType Leaf
    ) "Review surface '$relative' is missing."
}

$reviewSkill = Get-TextMetric ([string]$contract.review.skillPath)
Add-Check (
    $reviewSkill.lines -le 200 -and
    $reviewSkill.bytes -le 8192
) 'The review skill exceeds 200 lines or 8 KiB.'
Add-Check (
    $reviewSkill.content -match 'Get-ApplicableInstructions\.ps1' -and
    $reviewSkill.content -match 'Get-ValidationInventory\.ps1'
) 'The review skill does not resolve instructions and validation dynamically.'

foreach ($relative in @(
    '.github/skills/impeccable/SKILL.md',
    '.github/agents/impeccable-asset-producer.agent.md',
    '.github/agents/impeccable-documenter.agent.md',
    '.github/agents/impeccable-finish-reviewer.agent.md',
    '.github/agents/impeccable-manual-edit-applier.agent.md',
    '.github/instructions/impeccable-workflow.instructions.md',
    '.github/instructions/web-ux-resilience.instructions.md',
    'docs/impeccable-design.md',
    'docs/ux-design.md'
)) {
    Add-Check (
        Test-Path -LiteralPath (
            Join-Path $ProjectRoot $relative.Replace(
                '/',
                [IO.Path]::DirectorySeparatorChar)
        ) -PathType Leaf
    ) "Impeccable guidance surface '$relative' is missing."
}
Add-Check (-not (Test-Path -LiteralPath (
    Join-Path $ProjectRoot '.github' 'hooks' 'impeccable.json'
))) 'Impeccable hook is installed although ADR-0006 keeps it disabled.'
Add-Check (-not (Test-Path -LiteralPath (
    Join-Path $ProjectRoot '.github' 'workflows' 'impeccable-audit.yml'
))) 'Impeccable audit is installed although ADR-0006 keeps it disabled.'
$impeccableConfigPath = Join-Path $ProjectRoot '.impeccable' 'config.json'
Add-Check (Test-Path -LiteralPath $impeccableConfigPath -PathType Leaf) (
    'Impeccable shared configuration is missing.')
if (Test-Path -LiteralPath $impeccableConfigPath -PathType Leaf) {
    try {
        $impeccableConfig = Get-Content -LiteralPath $impeccableConfigPath `
            -Raw -Encoding UTF8 |
            ConvertFrom-Json
        Add-Check (
            $impeccableConfig.PSObject.Properties['updateCheck'] -and
            $impeccableConfig.updateCheck -eq $false
        ) 'Impeccable independent update checks must remain disabled.'
    } catch {
        $errors.Add("Impeccable configuration is invalid: $($_.Exception.Message)")
    }
}

$generatedRelativeRoot = [string]@($contract.generatedMirrors)[0].path
$generatedRoot = Join-Path $ProjectRoot $generatedRelativeRoot.Replace(
    '/',
    [IO.Path]::DirectorySeparatorChar)
Add-Check (Test-Path -LiteralPath $generatedRoot -PathType Container) (
    'Generated Claude rule directory is missing.')
$generatedFiles = @(
    if (Test-Path -LiteralPath $generatedRoot -PathType Container) {
        Get-ChildItem -LiteralPath $generatedRoot -Recurse -Filter '*.md' -File |
            Sort-Object FullName
    }
)
Add-Check ($generatedFiles.Count -eq $instructionFiles.Count) (
    "Generated Claude rule count $($generatedFiles.Count) does not match instruction count $($instructionFiles.Count).")
$expectedGenerated = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($instruction in $instructionFiles) {
    $sourceRelative = Get-RelativePath $instruction.FullName
    $subPath = $sourceRelative.Substring('.github/instructions/'.Length)
    $generatedRelative = $generatedRelativeRoot.TrimEnd('/') + '/' + (
        $subPath -replace '\.instructions\.md$', '.md')
    [void]$expectedGenerated.Add($generatedRelative)
    $generatedPath = Join-Path $ProjectRoot $generatedRelative.Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    Add-Check (Test-Path -LiteralPath $generatedPath -PathType Leaf) (
        "Generated Claude rule '$generatedRelative' is missing.")
    if (Test-Path -LiteralPath $generatedPath -PathType Leaf) {
        $generatedContent = Get-Content -LiteralPath $generatedPath -Raw -Encoding UTF8
        Add-Check (
            $generatedContent -match [regex]::Escape(
                "# AUTO-GENERATED from $sourceRelative")
        ) "Generated Claude rule '$generatedRelative' has stale provenance."
        foreach ($linkMatch in [regex]::Matches(
            $generatedContent,
            '\]\((?<target>[^)]+)\)'
        )) {
            $target = (
                $linkMatch.Groups['target'].Value -split '\s+'
            )[0].Trim('<', '>')
            if (
                [string]::IsNullOrWhiteSpace($target) -or
                $target.StartsWith('#') -or
                $target -match '^[a-z][a-z0-9+.-]*:'
            ) {
                continue
            }
            $target = ($target -split '[?#]')[0]
            if (-not [IO.Path]::GetExtension($target).Equals(
                '.md',
                [StringComparison]::OrdinalIgnoreCase
            )) {
                continue
            }
            $resolved = [IO.Path]::GetFullPath(
                (Join-Path (Split-Path -Parent $generatedPath) $target))
            Add-Check (
                $resolved.StartsWith(
                    $ProjectRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase) -and
                (Test-Path -LiteralPath $resolved -PathType Leaf)
            ) "Generated Claude rule '$generatedRelative' has a broken link '$target'."
        }
    }
}
foreach ($generated in $generatedFiles) {
    Add-Check (
        $expectedGenerated.Contains((Get-RelativePath $generated.FullName))
    ) "Generated Claude rule '$(Get-RelativePath $generated.FullName)' has no instruction source."
}

if ($errors.Count -gt 0) {
    throw "Guidance contract validation failed:`n$($errors -join "`n")"
}

$maxContext = $contextMetrics |
    Sort-Object lines, bytes -Descending |
    Select-Object -First 1
$result = [PSCustomObject]@{
    checks = $checks
    docs = $allDocs.Count
    adrs = $adrRecords.Count
    instructions = $instructionRecords.Count
    generatedMirrors = $generatedFiles.Count
    maximumMatchedContext = $maxContext
}

if ($Json) {
    $result | ConvertTo-Json -Depth 12
} else {
    $result
}
