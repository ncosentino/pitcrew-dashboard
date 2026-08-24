#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [Parameter(Mandatory)]
    [string]$PitCrewSourceRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

$plan = Read-SupportCanaryPlan -RunRoot $RunRoot
Assert-SupportCanaryCommit `
    -SourceRoot $DashboardSourceRoot `
    -ExpectedCommit ([string]$plan.dashboard.commit)
Assert-SupportCanaryCommit `
    -SourceRoot $PitCrewSourceRoot `
    -ExpectedCommit ([string]$plan.pitCrew.commit)

foreach ($project in @(
        'PitCrew.Support.Canary.AppHost',
        'PitCrew.Support.Agent.App',
        'PitCrew.Support.Broker.App'
    )) {
    $projectPath = Join-Path `
        $DashboardSourceRoot `
        "src/$project/$project.csproj"
    Invoke-SupportCanaryNative dotnet @(
        'build',
        $projectPath,
        '--configuration',
        $Configuration,
        '--nologo'
    )
}

if ([string]$plan.topologyProfile -ceq 'windows-installed') {
    if (-not $IsWindows) {
        throw 'The windows-installed canary profile requires Windows.'
    }
    Invoke-SupportCanaryNative pwsh @(
        '-NoProfile',
        '-File',
        (Join-Path $DashboardSourceRoot 'scripts/package-support-plane.ps1'),
        '-Configuration',
        $Configuration,
        '-Version',
        '0.0.0-canary',
        '-RuntimeIdentifiers',
        'win-x64',
        '-OutputRoot',
        (Join-Path $RunRoot 'artifacts/support-plane')
    )
}

if ([string]$plan.topologyProfile -ceq 'containerized') {
    if (-not $IsLinux) {
        throw 'The containerized canary profile requires a Linux host.'
    }
    $dockerOperatingSystem = [string](
        & docker info --format '{{.OSType}}'
    )
    $dockerOperatingSystem = $dockerOperatingSystem.Trim()
    if ($LASTEXITCODE -ne 0 -or
        $dockerOperatingSystem -cne 'linux') {
        throw 'The containerized canary profile requires a Linux container engine.'
    }
    $runId = [string]$plan.runId
    $dashboardImage = "pitcrew-support-canary-dashboard:$runId"
    $relayImage = "pitcrew-support-canary-relay:$runId"
    foreach ($image in @($dashboardImage, $relayImage)) {
        if (Test-SupportCanaryDockerObject -Kind image -Identity $image) {
            throw 'A run-scoped candidate container image already exists.'
        }
    }
    $builtImages = [Collections.Generic.List[string]]::new()
    $buildSucceeded = $false
    try {
        Invoke-SupportCanaryNativeBounded `
            -FilePath docker `
            -TimeoutSeconds 1200 `
            -ArgumentList @(
            'build',
            '--file',
            (Join-Path $DashboardSourceRoot 'Dockerfile'),
            '--tag',
            $dashboardImage,
            '--label',
            "io.pitcrew.canary.run-id=$runId",
            '--label',
            'io.pitcrew.canary.component=dashboard',
            '--label',
            "org.opencontainers.image.revision=$($plan.dashboard.commit)",
            $DashboardSourceRoot
        )
        $builtImages.Add($dashboardImage)
        Invoke-SupportCanaryNativeBounded `
            -FilePath docker `
            -TimeoutSeconds 1200 `
            -ArgumentList @(
            'build',
            '--file',
            (Join-Path (
                Join-Path $DashboardSourceRoot 'src/PitCrew.Support.Relay.App'
            ) 'Dockerfile'),
            '--tag',
            $relayImage,
            '--label',
            "io.pitcrew.canary.run-id=$runId",
            '--label',
            'io.pitcrew.canary.component=relay',
            '--label',
            "org.opencontainers.image.revision=$($plan.dashboard.commit)",
            $DashboardSourceRoot
        )
        $builtImages.Add($relayImage)
        $dashboardImageId = [string](& docker image inspect `
            --format '{{.Id}}' `
            $dashboardImage)
        $dashboardImageId = $dashboardImageId.Trim()
        $relayImageId = [string](& docker image inspect `
            --format '{{.Id}}' `
            $relayImage)
        $relayImageId = $relayImageId.Trim()
        if ($LASTEXITCODE -ne 0 -or
            $dashboardImageId -cnotmatch '^sha256:[a-f0-9]{64}$' -or
            $relayImageId -cnotmatch '^sha256:[a-f0-9]{64}$') {
            throw 'The candidate container image identity is invalid.'
        }
        $topology = [PSCustomObject][ordered]@{
            schemaVersion = 1
            runId = $runId
            dashboard = $plan.dashboard
            dashboardImage = [PSCustomObject][ordered]@{
                reference = $dashboardImage
                imageId = $dashboardImageId
            }
            relayImage = [PSCustomObject][ordered]@{
                reference = $relayImage
                imageId = $relayImageId
            }
            dashboardContainerName = "pitcrew-canary-$runId-dashboard"
            relayContainerName = "pitcrew-canary-$runId-relay"
            dashboardVolumeName = "pitcrew-canary-$runId-dashboard-data"
            relayVolumeName = "pitcrew-canary-$runId-relay-data"
            createdAt = [DateTimeOffset]::UtcNow.ToString('O')
        }
        Write-SupportCanaryJson `
            -LiteralPath (Join-Path $RunRoot 'container-topology.json') `
            -Value $topology
        $validatedTopology = Read-SupportCanaryContainerTopology `
            -RunRoot $RunRoot
        Assert-SupportCanaryContainerImage `
            -Identity $validatedTopology.dashboardImage `
            -DashboardCommit ([string]$plan.dashboard.commit) `
            -RunId $runId `
            -Component dashboard
        Assert-SupportCanaryContainerImage `
            -Identity $validatedTopology.relayImage `
            -DashboardCommit ([string]$plan.dashboard.commit) `
            -RunId $runId `
            -Component relay
        $buildSucceeded = $true
    } finally {
        if (-not $buildSucceeded) {
            foreach ($image in $builtImages) {
                & docker image rm --force $image
                if ($LASTEXITCODE -ne 0) {
                    throw 'Failed to remove an incomplete canary image build.'
                }
            }
        }
    }
}
