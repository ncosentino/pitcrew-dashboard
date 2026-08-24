#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$Version = '0.0.0-local',

    [string[]]$RuntimeIdentifiers = @(
        'linux-x64',
        'linux-arm64',
        'win-x64',
        'win-arm64'
    ),

    [string]$OutputRoot = 'artifacts/support-plane'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRootPath = if ([IO.Path]::IsPathFullyQualified($OutputRoot)) {
    [IO.Path]::GetFullPath($OutputRoot)
} else {
    Join-Path $repositoryRoot $OutputRoot
}
$publishRoot = Join-Path $outputRootPath 'publish'
$archiveRoot = Join-Path $outputRootPath 'archives'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ArgumentList
    )

    $global:LASTEXITCODE = 0
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' exited with code $LASTEXITCODE."
    }
}

function New-Archive {
    param(
        [Parameter(Mandatory)]
        [string]$Component,

        [Parameter(Mandatory)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory)]
        [string]$SourceDirectory
    )

    $assetName = "pitcrew-support-$Component-$Version-$RuntimeIdentifier.tar.gz"
    $assetPath = Join-Path $archiveRoot $assetName
    if (Test-Path -LiteralPath $assetPath -PathType Leaf) {
        Remove-Item -LiteralPath $assetPath -Force
    }
    Invoke-Checked tar @('-C', $SourceDirectory, '-czf', $assetPath, '.')
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
    Set-Content `
        -LiteralPath "$assetPath.sha256" `
        -Value "$hash  $assetName" `
        -Encoding utf8NoBOM `
        -NoNewline
    [PSCustomObject]@{
        Component = $Component
        RuntimeIdentifier = $RuntimeIdentifier
        Archive = [IO.Path]::GetRelativePath($repositoryRoot, $assetPath)
        Checksum = [IO.Path]::GetRelativePath($repositoryRoot, "$assetPath.sha256")
        Sha256 = $hash
    }
}

$components = @(
    @{
        Name = 'agent'
        Project = 'src/PitCrew.Support.Agent.App/PitCrew.Support.Agent.App.csproj'
    },
    @{
        Name = 'broker'
        Project = 'src/PitCrew.Support.Broker.App/PitCrew.Support.Broker.App.csproj'
    },
    @{
        Name = 'relay'
        Project = 'src/PitCrew.Support.Relay.App/PitCrew.Support.Relay.App.csproj'
    }
)

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
$manifest = [System.Collections.Generic.List[object]]::new()
foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
    foreach ($component in $components) {
        $publishDirectory = Join-Path $publishRoot (
            "$($component.Name)-$runtimeIdentifier"
        )
        if (Test-Path -LiteralPath $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
        }
        Invoke-Checked dotnet @(
            'publish',
            (Join-Path $repositoryRoot $component.Project),
            '--configuration',
            $Configuration,
            '--runtime',
            $runtimeIdentifier,
            '--self-contained',
            'true',
            '-p:PublishSingleFile=true',
            '-p:DebugType=None',
            '-p:DebugSymbols=false',
            '--output',
            $publishDirectory
        )
        if ($component.Name -eq 'agent') {
            Copy-Item `
                -LiteralPath (
                    Join-Path $repositoryRoot `
                        'deploy/support-plane/support-agent.env.example'
                ) `
                -Destination (
                    Join-Path $publishDirectory 'support-agent.env.example'
                )
        }
        $manifest.Add((New-Archive `
            -Component $component.Name `
            -RuntimeIdentifier $runtimeIdentifier `
            -SourceDirectory $publishDirectory))
    }
    $installerDirectory = Join-Path $publishRoot "installer-$runtimeIdentifier"
    if (Test-Path -LiteralPath $installerDirectory) {
        Remove-Item -LiteralPath $installerDirectory -Recurse -Force
    }
    New-Item `
        -ItemType Directory `
        -Path $installerDirectory `
        -Force |
        Out-Null
    Copy-Item `
        -LiteralPath (
            Join-Path $repositoryRoot 'scripts' 'Install-PitCrewSupportPlane.ps1'
        ) `
        -Destination $installerDirectory
    Copy-Item `
        -LiteralPath (
            Join-Path `
                $repositoryRoot `
                'assets' `
                'support-plane' `
                'support-evidence-policy-v0.10.8.json'
        ) `
        -Destination $installerDirectory
    $manifest.Add((New-Archive `
        -Component 'installer' `
        -RuntimeIdentifier $runtimeIdentifier `
        -SourceDirectory $installerDirectory))
}

$manifestPath = Join-Path $outputRootPath 'support-plane-packages.json'
$manifest |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "Wrote support-plane package manifest: $manifestPath"
$manifest
