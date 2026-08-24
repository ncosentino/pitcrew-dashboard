#Requires -Version 7.0
using namespace System.IO

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (
    Join-Path `
        $repositoryRoot `
        'scripts/release/PublishedReleaseVerification.psm1'
) -Force
$errors = [Collections.Generic.List[string]]::new()
$checks = 0

function Add-Check {
    param(
        [object]$Condition,
        [Parameter(Mandatory)][string]$Failure
    )

    $script:checks++
    if (-not [bool]$Condition) {
        $script:errors.Add($Failure)
    }
}

function Add-RejectionCheck {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Operation
    )

    $script:checks++
    try {
        & $Operation
        $script:errors.Add("$Name was accepted.")
    } catch [InvalidDataException] {
        Write-Verbose "$Name rejected."
    }
}

function Copy-Fixture {
    param([Parameter(Mandatory)][object]$Value)

    return $Value |
        ConvertTo-Json -Depth 20 -Compress |
        ConvertFrom-Json -Depth 20
}

$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "pitcrew-published-release-tests-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $archivePath = Join-Path $temporaryRoot 'example-1.2.3-linux-x64.tar.gz'
    [IO.File]::WriteAllBytes(
        $archivePath,
        [Text.Encoding]::UTF8.GetBytes('archive-payload'))
    $archiveHash = (
        Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $sidecarPath = "$archivePath.sha256"
    [IO.File]::WriteAllText(
        $sidecarPath,
        "$archiveHash  $(Split-Path $archivePath -Leaf)",
        [Text.UTF8Encoding]::new($false))
    $local = @(
        Get-LocalReleaseAssetEvidence -FilePath @(
            $archivePath,
            $sidecarPath
        )
    )
    Assert-LocalArchiveSidecars -LocalEvidence $local
    Add-Check ($local.Count -eq 2) (
        'Local asset evidence omitted an archive or sidecar.')

    $release = [PSCustomObject][ordered]@{
        id = 42
        draft = $false
        target_commitish = [string]::new('a', 40)
        assets = @(
            $local |
                ForEach-Object {
                    [PSCustomObject][ordered]@{
                        name = $_.Name
                        state = 'uploaded'
                        size = $_.Length
                        digest = "sha256:$($_.Sha256)"
                    }
                }
        )
    }
    $published = Assert-PublishedReleaseAssets `
        -Release $release `
        -LocalEvidence $local `
        -ExpectedAssetName @($local.Name) `
        -ExpectedReleaseSha ([string]::new('a', 40))
    Add-Check ($published.AssetCount -eq 2) (
        'Published asset verification returned the wrong count.')

    [IO.File]::WriteAllText(
        $sidecarPath,
        "$([string]::new('b', 64))  $(Split-Path $archivePath -Leaf)",
        [Text.UTF8Encoding]::new($false))
    $badSidecar = @(
        Get-LocalReleaseAssetEvidence -FilePath @(
            $archivePath,
            $sidecarPath
        )
    )
    Add-RejectionCheck 'Sidecar mismatch' {
        Assert-LocalArchiveSidecars -LocalEvidence $badSidecar
    }
    [IO.File]::WriteAllText(
        $sidecarPath,
        "$archiveHash  $(Split-Path $archivePath -Leaf)",
        [Text.UTF8Encoding]::new($false))

    $badDigestRelease = Copy-Fixture $release
    $badDigestRelease.assets[0].digest =
        "sha256:$([string]::new('c', 64))"
    Add-RejectionCheck 'Published digest mismatch' {
        Assert-PublishedReleaseAssets `
            -Release $badDigestRelease `
            -LocalEvidence $local
    }
    Add-RejectionCheck 'Published inventory mismatch' {
        Assert-PublishedReleaseAssets `
            -Release $release `
            -LocalEvidence $local `
            -ExpectedAssetName @($local.Name + 'unexpected.tar.gz')
    }
    Add-RejectionCheck 'Release commit mismatch' {
        Assert-PublishedReleaseAssets `
            -Release $release `
            -LocalEvidence $local `
            -ExpectedReleaseSha ([string]::new('d', 40))
    }

    $amd64Digest = "sha256:$([string]::new('1', 64))"
    $arm64Digest = "sha256:$([string]::new('2', 64))"
    $index = [PSCustomObject][ordered]@{
        schemaVersion = 2
        mediaType = 'application/vnd.oci.image.index.v1+json'
        manifests = @(
            [PSCustomObject][ordered]@{
                mediaType = 'application/vnd.oci.image.manifest.v1+json'
                digest = $amd64Digest
                size = 100
                platform = [PSCustomObject][ordered]@{
                    os = 'linux'
                    architecture = 'amd64'
                }
                annotations = [PSCustomObject]@{}
            },
            [PSCustomObject][ordered]@{
                mediaType = 'application/vnd.oci.image.manifest.v1+json'
                digest = "sha256:$([string]::new('3', 64))"
                size = 50
                platform = [PSCustomObject][ordered]@{
                    os = 'unknown'
                    architecture = 'unknown'
                }
                annotations = [PSCustomObject][ordered]@{
                    'vnd.docker.reference.type' = 'attestation-manifest'
                    'vnd.docker.reference.digest' = $amd64Digest
                }
            },
            [PSCustomObject][ordered]@{
                mediaType = 'application/vnd.oci.image.manifest.v1+json'
                digest = $arm64Digest
                size = 100
                platform = [PSCustomObject][ordered]@{
                    os = 'linux'
                    architecture = 'arm64'
                }
                annotations = [PSCustomObject]@{}
            },
            [PSCustomObject][ordered]@{
                mediaType = 'application/vnd.oci.image.manifest.v1+json'
                digest = "sha256:$([string]::new('4', 64))"
                size = 50
                platform = [PSCustomObject][ordered]@{
                    os = 'unknown'
                    architecture = 'unknown'
                }
                annotations = [PSCustomObject][ordered]@{
                    'vnd.docker.reference.type' = 'attestation-manifest'
                    'vnd.docker.reference.digest' = $arm64Digest
                }
            }
        )
    }
    $indexDigest = "sha256:$([string]::new('f', 64))"
    $container = Assert-PublishedContainerIndex `
        -ExpectedDigest $indexDigest `
        -SemanticDigest $indexDigest `
        -ImmutableDigest $indexDigest `
        -Index $index
    Add-Check (
        $container.AttestationCount -eq 2 -and
        $container.RuntimePlatforms.Count -eq 2
    ) 'Container verification omitted a platform or attestation.'

    Add-RejectionCheck 'Container tag mismatch' {
        Assert-PublishedContainerIndex `
            -ExpectedDigest $indexDigest `
            -SemanticDigest $indexDigest `
            -ImmutableDigest "sha256:$([string]::new('e', 64))" `
            -Index $index
    }
    $missingPlatform = Copy-Fixture $index
    $missingPlatform.manifests = @($missingPlatform.manifests[0..1])
    Add-RejectionCheck 'Container platform omission' {
        Assert-PublishedContainerIndex `
            -ExpectedDigest $indexDigest `
            -SemanticDigest $indexDigest `
            -ImmutableDigest $indexDigest `
            -Index $missingPlatform
    }
    $wrongAttestation = Copy-Fixture $index
    $wrongAnnotations = $wrongAttestation.manifests[3].annotations
    $wrongAnnotations.'vnd.docker.reference.digest' = $amd64Digest
    Add-RejectionCheck 'Container attestation mismatch' {
        Assert-PublishedContainerIndex `
            -ExpectedDigest $indexDigest `
            -SemanticDigest $indexDigest `
            -ImmutableDigest $indexDigest `
            -Index $wrongAttestation
    }
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

if ($errors.Count -gt 0) {
    throw "Published release verification tests failed:`n$($errors -join "`n")"
}
Write-Host "Published release verification tests passed: $checks checks."
