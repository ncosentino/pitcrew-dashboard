#Requires -Version 7.0
using namespace System.IO

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern(
        '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
    )]
    [string]$ReleaseTag,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$FilePath,

    [string[]]$ExpectedAssetName = @(),

    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$ExpectedReleaseSha = '',

    [ValidateRange(1, 30)]
    [int]$MaximumAttempts = 12,

    [ValidateRange(1, 30)]
    [int]$RetryDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY) -or
    $env:GITHUB_REPOSITORY -cnotmatch
        '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'GITHUB_REPOSITORY must identify the release repository.'
}
Import-Module (
    Join-Path $PSScriptRoot 'PublishedReleaseVerification.psm1'
) -Force
$localEvidence = @(
    Get-LocalReleaseAssetEvidence -FilePath $FilePath
)
Assert-LocalArchiveSidecars -LocalEvidence $localEvidence
$encodedTag = [Uri]::EscapeDataString($ReleaseTag)
$lastFailure = $null
for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
    $releaseJson = gh api (
        "repos/$env:GITHUB_REPOSITORY/releases/tags/$encodedTag"
    ) | Out-String
    if ($LASTEXITCODE -ne 0 -or $releaseJson.Length -gt 4MB) {
        $lastFailure = [InvalidDataException]::new(
            'GitHub release metadata is unavailable or oversized.')
    } else {
        try {
            $release = $releaseJson | ConvertFrom-Json -Depth 20
            $verified = Assert-PublishedReleaseAssets `
                -Release $release `
                -LocalEvidence $localEvidence `
                -ExpectedAssetName $ExpectedAssetName `
                -ExpectedReleaseSha $ExpectedReleaseSha
            Write-Host (
                "Verified $($verified.AssetCount) published release assets " +
                "for $ReleaseTag.")
            return
        } catch [InvalidDataException] {
            $lastFailure = $_.Exception
        }
    }
    if ($attempt -lt $MaximumAttempts) {
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}
throw [InvalidDataException]::new(
    'Published release asset verification did not converge.',
    $lastFailure)
