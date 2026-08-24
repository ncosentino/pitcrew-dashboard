using namespace System.IO

Set-StrictMode -Version Latest

$script:sha256Pattern = '^sha256:[a-f0-9]{64}$'
$script:assetNamePattern = '^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$'

function Get-LocalReleaseAssetEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string[]]$FilePath
    )

    if ($FilePath.Count -gt 200) {
        throw [InvalidDataException]::new(
            'The local release asset inventory exceeds its bound.')
    }
    $comparison = if ($IsWindows) {
        [StringComparer]::OrdinalIgnoreCase
    } else {
        [StringComparer]::Ordinal
    }
    $paths = [Collections.Generic.HashSet[string]]::new($comparison)
    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $evidence = [Collections.Generic.List[object]]::new()
    foreach ($candidate in $FilePath) {
        $path = [IO.Path]::GetFullPath($candidate)
        if (-not $paths.Add($path)) {
            throw [InvalidDataException]::new(
                'The local release asset inventory contains a duplicate path.')
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw [InvalidDataException]::new(
                'A local release asset is unavailable.')
        }
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $item.Length -le 0 -or
            $item.Length -gt 1GB -or
            $item.Name -cnotmatch $script:assetNamePattern -or
            -not $names.Add($item.Name)) {
            throw [InvalidDataException]::new(
                'A local release asset violates its bounded file contract.')
        }
        $evidence.Add([PSCustomObject][ordered]@{
            Name = $item.Name
            Path = $item.FullName
            Length = [int64]$item.Length
            Sha256 = (
                Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256
            ).Hash.ToLowerInvariant()
        })
    }
    if ($evidence.Count -eq 0) {
        throw [InvalidDataException]::new(
            'The local release asset inventory is empty.')
    }
    return $evidence.ToArray()
}

function Assert-LocalArchiveSidecars {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [object[]]$LocalEvidence
    )

    $byName = @{}
    foreach ($item in $LocalEvidence) {
        $byName[[string]$item.Name] = $item
    }
    foreach ($archive in @(
            $LocalEvidence |
                Where-Object {
                    ([string]$_.Name).EndsWith(
                        '.tar.gz',
                        [StringComparison]::Ordinal)
                }
        )) {
        $sidecarName = "$($archive.Name).sha256"
        if (-not $byName.ContainsKey($sidecarName)) {
            throw [InvalidDataException]::new(
                'A release archive is missing its SHA-256 sidecar.')
        }
        $sidecar = $byName[$sidecarName]
        if ([int64]$sidecar.Length -gt 512) {
            throw [InvalidDataException]::new(
                'A release archive sidecar exceeds its size bound.')
        }
        $content = (
            Get-Content -LiteralPath $sidecar.Path -Raw -Encoding UTF8
        ).TrimEnd("`r", "`n")
        $match = [regex]::Match(
            $content,
            '^(?<hash>[a-f0-9]{64})  (?<name>[A-Za-z0-9][A-Za-z0-9._-]{0,255})$',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant,
            [TimeSpan]::FromSeconds(1))
        if (-not $match.Success -or
            $match.Groups['hash'].Value -cne [string]$archive.Sha256 -or
            $match.Groups['name'].Value -cne [string]$archive.Name) {
            throw [InvalidDataException]::new(
                'A release archive sidecar does not match its archive.')
        }
    }
}

function Assert-PublishedReleaseAssets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object]$Release,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [object[]]$LocalEvidence,

        [string[]]$ExpectedAssetName = @(),

        [string]$ExpectedReleaseSha = ''
    )

    if (-not [string]::IsNullOrWhiteSpace($ExpectedReleaseSha) -and
        $ExpectedReleaseSha -cnotmatch '^[a-f0-9]{40}$') {
        throw [InvalidDataException]::new(
            'The expected release commit is invalid.')
    }
    if ([bool]$Release.draft -or
        @($Release.assets).Count -gt 200) {
        throw [InvalidDataException]::new(
            'The GitHub release is not a bounded published release.')
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedReleaseSha) -and
        [string]$Release.target_commitish -cne $ExpectedReleaseSha) {
        throw [InvalidDataException]::new(
            'The GitHub release does not target the expected commit.')
    }
    $publishedNames = @(
        @($Release.assets) |
            ForEach-Object { [string]$_.name } |
            Sort-Object
    )
    if ($ExpectedAssetName.Count -gt 0) {
        $expectedNames = @($ExpectedAssetName | Sort-Object)
        if ((@($publishedNames) -join "`n") -cne
            (@($expectedNames) -join "`n")) {
            throw [InvalidDataException]::new(
                'The published release asset inventory is incomplete or unexpected.')
        }
    }
    foreach ($local in $LocalEvidence) {
        $matches = @(
            @($Release.assets) |
                Where-Object {
                    [string]$_.name -ceq [string]$local.Name
                }
        )
        if ($matches.Count -ne 1) {
            throw [InvalidDataException]::new(
                'A local release asset does not have one published counterpart.')
        }
        $asset = $matches[0]
        if ([string]$asset.state -cne 'uploaded' -or
            [int64]$asset.size -ne [int64]$local.Length -or
            [string]$asset.digest -cne "sha256:$($local.Sha256)") {
            throw [InvalidDataException]::new(
                'A published release asset does not match its local digest and size.')
        }
    }
    return [PSCustomObject][ordered]@{
        AssetCount = $LocalEvidence.Count
        ReleaseId = [int64]$Release.id
    }
}

function Assert-PublishedContainerIndex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^sha256:[a-f0-9]{64}$')]
        [string]$ExpectedDigest,

        [Parameter(Mandatory)]
        [ValidatePattern('^sha256:[a-f0-9]{64}$')]
        [string]$SemanticDigest,

        [Parameter(Mandatory)]
        [ValidatePattern('^sha256:[a-f0-9]{64}$')]
        [string]$ImmutableDigest,

        [Parameter(Mandatory)]
        [object]$Index
    )

    if ($ExpectedDigest -cne $SemanticDigest -or
        $ExpectedDigest -cne $ImmutableDigest -or
        [int]$Index.schemaVersion -ne 2 -or
        [string]$Index.mediaType -notin @(
            'application/vnd.oci.image.index.v1+json',
            'application/vnd.docker.distribution.manifest.list.v2+json'
        )) {
        throw [InvalidDataException]::new(
            'Container tags do not resolve to the expected image index.')
    }
    $manifests = @($Index.manifests)
    if ($manifests.Count -lt 4 -or $manifests.Count -gt 8) {
        throw [InvalidDataException]::new(
            'The published image index has an invalid descriptor count.')
    }
    $images = @{}
    $attestationReferences = [Collections.Generic.List[string]]::new()
    $digests = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($descriptor in $manifests) {
        $digest = [string]$descriptor.digest
        if ($digest -cnotmatch $script:sha256Pattern -or
            [int64]$descriptor.size -le 0 -or
            -not $digests.Add($digest)) {
            throw [InvalidDataException]::new(
                'The published image index contains an invalid descriptor.')
        }
        $annotationsProperty =
            $descriptor.PSObject.Properties['annotations']
        $annotations = if ($null -eq $annotationsProperty) {
            $null
        } else {
            $annotationsProperty.Value
        }
        $referenceTypeProperty = if ($null -eq $annotations) {
            $null
        } else {
            $annotations.PSObject.Properties[
                'vnd.docker.reference.type']
        }
        $referenceType = if ($null -eq $referenceTypeProperty) {
            ''
        } else {
            [string]$referenceTypeProperty.Value
        }
        if ($referenceType -ceq 'attestation-manifest') {
            if ([string]$descriptor.platform.os -cne 'unknown' -or
                [string]$descriptor.platform.architecture -cne 'unknown') {
                throw [InvalidDataException]::new(
                    'An image attestation descriptor has an invalid platform.')
            }
            $referenceDigestProperty = $annotations.PSObject.Properties[
                'vnd.docker.reference.digest']
            $referenceDigest = if (
                $null -eq $referenceDigestProperty
            ) {
                ''
            } else {
                [string]$referenceDigestProperty.Value
            }
            if ($referenceDigest -cnotmatch $script:sha256Pattern) {
                throw [InvalidDataException]::new(
                    'An image attestation descriptor has an invalid subject.')
            }
            $attestationReferences.Add($referenceDigest)
            continue
        }
        $architecture = [string]$descriptor.platform.architecture
        if ([string]$descriptor.platform.os -cne 'linux' -or
            $architecture -notin @('amd64', 'arm64') -or
            $images.ContainsKey($architecture)) {
            throw [InvalidDataException]::new(
                'The published image index has an invalid runtime platform.')
        }
        $images[$architecture] = $digest
    }
    if ($images.Count -ne 2 -or
        $attestationReferences.Count -ne 2) {
        throw [InvalidDataException]::new(
            'The published image index omits a platform or SBOM attestation.')
    }
    $references = @($attestationReferences | Sort-Object)
    $expectedReferences = @($images.Values | Sort-Object)
    if ((@($references) -join "`n") -cne
        (@($expectedReferences) -join "`n")) {
        throw [InvalidDataException]::new(
            'The image attestations do not reference both runtime manifests.')
    }
    return [PSCustomObject][ordered]@{
        Digest = $ExpectedDigest
        RuntimePlatforms = @('linux/amd64', 'linux/arm64')
        AttestationCount = $attestationReferences.Count
    }
}

Export-ModuleMember -Function @(
    'Get-LocalReleaseAssetEvidence',
    'Assert-LocalArchiveSidecars',
    'Assert-PublishedReleaseAssets',
    'Assert-PublishedContainerIndex'
)
