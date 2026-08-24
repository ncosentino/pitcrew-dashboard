#Requires -Version 7.0
using namespace System.IO

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^ghcr\.io/[a-z0-9._-]+/[a-z0-9._-]+$')]
    [string]$ImageName,

    [Parameter(Mandatory)]
    [ValidatePattern(
        '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
    )]
    [string]$ReleaseTag,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$ReleaseSha,

    [string]$ExpectedDigest = '',

    [switch]$RequireProvenanceAttestation,

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
    throw 'GITHUB_REPOSITORY must identify the attestation repository.'
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedDigest) -and
    $ExpectedDigest -cnotmatch '^sha256:[a-f0-9]{64}$') {
    throw 'ExpectedDigest must be an SHA-256 digest when provided.'
}
Import-Module (
    Join-Path $PSScriptRoot 'PublishedReleaseVerification.psm1'
) -Force

$semanticReference = "$ImageName`:$($ReleaseTag.Substring(1))"
$immutableReference = "$ImageName`:sha-$ReleaseSha"
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "pitcrew-release-image-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $manifestPaths = @{
        Semantic = Join-Path $temporaryRoot 'semantic-index.json'
        Immutable = Join-Path $temporaryRoot 'immutable-index.json'
    }
    $lastFailure = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $succeeded = $true
        foreach ($entry in @(
                @{ Reference = $semanticReference; Path = $manifestPaths.Semantic },
                @{ Reference = $immutableReference; Path = $manifestPaths.Immutable }
            )) {
            $errorPath = "$($entry.Path).error"
            $process = Start-Process `
                -FilePath docker `
                -ArgumentList @(
                    'buildx',
                    'imagetools',
                    'inspect',
                    '--raw',
                    $entry.Reference
                ) `
                -NoNewWindow `
                -PassThru `
                -Wait `
                -RedirectStandardOutput $entry.Path `
                -RedirectStandardError $errorPath
            try {
                if ($process.ExitCode -ne 0) {
                    $succeeded = $false
                    break
                }
            } finally {
                $process.Dispose()
            }
        }
        if ($succeeded) {
            try {
                $semanticItem = Get-Item -LiteralPath $manifestPaths.Semantic
                $immutableItem = Get-Item -LiteralPath $manifestPaths.Immutable
                if ($semanticItem.Length -le 0 -or
                    $semanticItem.Length -gt 1MB -or
                    $immutableItem.Length -le 0 -or
                    $immutableItem.Length -gt 1MB) {
                    throw [InvalidDataException]::new(
                        'A published image index is empty or oversized.')
                }
                $semanticDigest = 'sha256:' + (
                    Get-FileHash `
                        -LiteralPath $semanticItem.FullName `
                        -Algorithm SHA256
                ).Hash.ToLowerInvariant()
                $immutableDigest = 'sha256:' + (
                    Get-FileHash `
                        -LiteralPath $immutableItem.FullName `
                        -Algorithm SHA256
                ).Hash.ToLowerInvariant()
                $resolvedExpected = if (
                    [string]::IsNullOrWhiteSpace($ExpectedDigest)
                ) {
                    $semanticDigest
                } else {
                    $ExpectedDigest
                }
                $index = Get-Content `
                    -LiteralPath $semanticItem.FullName `
                    -Raw `
                    -Encoding UTF8 |
                    ConvertFrom-Json -Depth 20
                $verified = Assert-PublishedContainerIndex `
                    -ExpectedDigest $resolvedExpected `
                    -SemanticDigest $semanticDigest `
                    -ImmutableDigest $immutableDigest `
                    -Index $index
                $lastFailure = $null
                break
            } catch [InvalidDataException] {
                $lastFailure = $_.Exception
            }
        } else {
            $lastFailure = [InvalidDataException]::new(
                'A published container tag is not yet available.')
        }
        if ($attempt -lt $MaximumAttempts) {
            Start-Sleep -Seconds $RetryDelaySeconds
        }
    }
    if ($null -ne $lastFailure) {
        throw [InvalidDataException]::new(
            'Published container index verification did not converge.',
            $lastFailure)
    }

    if ($RequireProvenanceAttestation) {
        $attestationVerified = $false
        for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
            gh attestation verify `
                "oci://$ImageName@$($verified.Digest)" `
                --repo $env:GITHUB_REPOSITORY `
                --signer-workflow (
                    "$env:GITHUB_REPOSITORY/.github/workflows/publish-container.yml"
                ) `
                --source-digest $ReleaseSha `
                --source-ref "refs/tags/$ReleaseTag" `
                --deny-self-hosted-runners |
                Out-Null
            if ($LASTEXITCODE -eq 0) {
                $attestationVerified = $true
                break
            }
            if ($attempt -lt $MaximumAttempts) {
                Start-Sleep -Seconds $RetryDelaySeconds
            }
        }
        if (-not $attestationVerified) {
            throw [InvalidDataException]::new(
                'Published container provenance verification did not converge.')
        }
    }
    Write-Host (
        "Verified $ImageName@$($verified.Digest) with " +
        "$($verified.AttestationCount) SBOM attestations.")
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
