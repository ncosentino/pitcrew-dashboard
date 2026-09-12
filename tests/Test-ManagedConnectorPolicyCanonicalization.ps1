#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InstallerPath = (
        Join-Path $PSScriptRoot '..' 'scripts' 'Enable-PitCrewCapacityOperations.ps1')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedInstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$tokens = $null
$parseErrors = $null
$installerAst = [Management.Automation.Language.Parser]::ParseFile(
    $resolvedInstallerPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "The managed connector installer has $($parseErrors.Count) parse error(s)."
}

$equivalenceFunction = $installerAst.Find(
    {
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Test-EquivalentStringSequence'
    },
    $true)
if ($null -eq $equivalenceFunction) {
    throw 'The managed connector installer equivalence function was not found.'
}
Invoke-Expression $equivalenceFunction.Extent.Text

$managedConnectorSettings = [pscustomobject]@{}
$script:StoredProfiles = @()

function Get-ManagedConnectorSetting {
    return $script:StoredProfiles
}

$projectionNames = @(
    'existingProfiles'
    'existingRecoveryProfiles'
    'existingImageRolloutProfiles'
)
$requestedProfiles = @(
    @('PROFILE-A', ' profile-b ', 'profile-a') |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Sort-Object -Unique
)

foreach ($projectionName in $projectionNames) {
    $assignment = $installerAst.Find(
        {
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -eq $projectionName
        },
        $true)
    if ($null -eq $assignment) {
        throw "The '$projectionName' managed policy projection was not found."
    }

    $projection = [scriptblock]::Create($assignment.Right.Extent.Text)
    $script:StoredProfiles = @(' Profile-B ', 'profile-a', 'PROFILE-B')
    $existingProfiles = @(& $projection)
    if ($existingProfiles.Count -ne 2 -or
        ($existingProfiles -join ',') -cne 'profile-a,profile-b') {
        throw "The '$projectionName' projection did not normalize case, remove duplicates, and sort stored profiles."
    }
    if (-not (Test-EquivalentStringSequence `
            -Left $requestedProfiles `
            -Right $existingProfiles)) {
        throw "The '$projectionName' projection rejected an equivalent reordered policy."
    }

    $script:StoredProfiles = @('profile-b', 'profile-a', 'profile-c')
    $widenedProfiles = @(& $projection)
    if (Test-EquivalentStringSequence `
            -Left $requestedProfiles `
            -Right $widenedProfiles) {
        throw "The '$projectionName' projection accepted a widened policy."
    }
}

Write-Host 'Managed connector policy canonicalization validation passed: 3 projections.' -ForegroundColor Green
