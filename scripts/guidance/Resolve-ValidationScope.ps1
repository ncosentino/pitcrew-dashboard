#Requires -Version 7.0
<#
.SYNOPSIS
    Resolve Dashboard CI scope for one workflow event.

.DESCRIPTION
    Returns one of: full, subset, ready-only, guidance-only.
    Uses changed-path evidence to determine whether a guidance-only scope is safe.
    Falls back to full for missing, ambiguous, mixed, or unknown path evidence.

.OUTPUTS
    [string] The resolved validation scope.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('pull_request', 'push', 'workflow_dispatch')]
    [string]$EventName,

    [string]$RequestedScope = '',
    [bool]$IsDraft = $false,

    [ValidateSet('full', 'subset', 'ready-only')]
    [string]$DraftMode = 'subset',

    [string[]]$ChangedFiles = @(),
    [switch]$Conservative
)

$ErrorActionPreference = 'Stop'

# ── Path classification patterns ──
# Guidance-only: documentation, design authority, instructions, generated mirrors,
# ADRs, guidance scripts/tests, and Impeccable design artifacts.
$guidanceOnlyPatterns = @(
    '^AGENTS\.md$'
    '^CLAUDE\.md$'
    '^\.claude/CLAUDE\.md$'
    '^\.claude/rules/'
    '^\.github/copilot-instructions\.md$'
    '^\.github/instructions/'
    '^\.github/agents/'
    '^\.github/skills/'
    '^\.github/genesis-guidance'
    '^PRODUCT\.md$'
    '^DESIGN\.md$'
    '^\.impeccable/'
    '^docs/'
    '^scripts/guidance/'
    '^tests/Test-Guidance\.ps1$'
)

# Full-trigger: any of these force full validation regardless of other paths.
$fullTriggerPatterns = @(
    '\.csproj$'
    '\.slnx$'
    '\.cs$'
    '\.tsx?$'
    '\.jsx?$'
    '\.mjs$'
    '\.css$'
    '^src/'
    '^tests/(?!Test-Guidance\.ps1$)'
    '^package\.json$'
    '^package-lock\.json$'
    '^Directory\.'
    '^global\.json$'
    '^Dockerfile$'
    '^docker-compose'
    '^deploy/'
    '^\.container/'
    '^\.github/workflows/'
    '^\.github/actions/'
    '^\.github/genesis-delivery'
    '^\.githooks/'
    '^scripts/(?!guidance/)'
    '^\.dockerignore$'
    '^\.env\.'
    '^BannedSymbols\.txt$'
    '^Start-LocalDashboard\.ps1$'
    '^user-features/'
    '^assets/'
)

function Test-PathMatchesAny {
    param([string]$Path, [string[]]$Patterns)
    $normalized = $Path.Replace('\', '/')
    foreach ($pattern in $Patterns) {
        if ($normalized -match $pattern) { return $true }
    }
    return $false
}

function Resolve-PathScope {
    <#
    .SYNOPSIS
        Classify changed files and return guidance-only or full.
    .OUTPUTS
        [PSCustomObject] with Scope and Categories properties.
    #>
    param([string[]]$Files)

    if ($Files.Count -eq 0) {
        return [PSCustomObject]@{ Scope = 'full'; Categories = @('no-evidence') }
    }

    $categories = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($file in $Files) {
        if (Test-PathMatchesAny $file $fullTriggerPatterns) {
            [void]$categories.Add('runtime')
            return [PSCustomObject]@{ Scope = 'full'; Categories = @($categories) }
        }
        if (Test-PathMatchesAny $file $guidanceOnlyPatterns) {
            [void]$categories.Add('guidance')
        } else {
            [void]$categories.Add('unknown')
        }
    }

    if ($categories.Contains('unknown')) {
        return [PSCustomObject]@{ Scope = 'full'; Categories = @($categories) }
    }

    return [PSCustomObject]@{ Scope = 'guidance-only'; Categories = @($categories) }
}

# ── Main dispatch ──

if ($EventName -eq 'workflow_dispatch') {
    $scope = if ($RequestedScope) { $RequestedScope } else { 'full' }
    if ($scope -notin @('full', 'subset', 'guidance-only')) {
        Write-Error "Unsupported requested validation scope '$scope'."
    }
    return $scope
}

if ($IsDraft) {
    return $DraftMode
}

# For both pull_request (ready) and push: use path evidence.
if ($Conservative -or $ChangedFiles.Count -eq 0) {
    return 'full'
}

$result = Resolve-PathScope -Files $ChangedFiles
return $result.Scope
