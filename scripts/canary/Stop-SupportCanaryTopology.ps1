#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [ValidateRange(5, 60)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

function Update-ContainerRuntimeRecord {
    param(
        [Parameter(Mandatory)]
        [string]$RunRoot,

        [Parameter(Mandatory)]
        [object]$Plan,

        [Parameter(Mandatory)]
        [object]$Topology
    )

    $dashboardContainerId = [string](
        & docker container inspect `
            --format '{{.Id}}' `
            ([string]$Topology.dashboardContainerName)
    )
    $relayContainerId = [string](
        & docker container inspect `
            --format '{{.Id}}' `
            ([string]$Topology.relayContainerName)
    )
    $dashboardContainerId = $dashboardContainerId.Trim()
    $relayContainerId = $relayContainerId.Trim()
    if ($LASTEXITCODE -ne 0 -or
        $dashboardContainerId -cnotmatch '^[a-f0-9]{64}$' -or
        $relayContainerId -cnotmatch '^[a-f0-9]{64}$') {
        throw 'The current canary container IDs are unavailable.'
    }
    Assert-SupportCanaryContainerInstance `
        -ContainerId $dashboardContainerId `
        -ExpectedImageId ([string]$Topology.dashboardImage.imageId) `
        -RunId ([string]$Plan.runId) `
        -Component dashboard
    Assert-SupportCanaryContainerInstance `
        -ContainerId $relayContainerId `
        -ExpectedImageId ([string]$Topology.relayImage.imageId) `
        -RunId ([string]$Plan.runId) `
        -Component relay
    Write-SupportCanaryJson `
        -LiteralPath (Join-Path $RunRoot 'container-runtime.json') `
        -Value ([PSCustomObject][ordered]@{
            schemaVersion = 1
            runId = [string]$Plan.runId
            dashboardContainerId = $dashboardContainerId
            relayContainerId = $relayContainerId
        }) `
        -Overwrite
}

$plan = Read-SupportCanaryPlan -RunRoot $RunRoot
$containerTopology = if (
    [string]$plan.topologyProfile -ceq 'containerized'
) {
    Read-SupportCanaryContainerTopology -RunRoot $RunRoot
} else {
    $null
}
$processPath = Join-Path $RunRoot 'topology-process.json'
$hasProcessState = Test-Path -LiteralPath $processPath -PathType Leaf
if (-not $hasProcessState -and
    [string]$plan.topologyProfile -cne 'containerized') {
    throw 'The canary topology process record is unavailable.'
}
$process = $null
if ($hasProcessState) {
    $state = Get-Content -LiteralPath $processPath -Raw -Encoding utf8 |
        ConvertFrom-Json -Depth 10 -ErrorAction Stop
    if ($state.schemaVersion -ne 1 -or
        [string]$state.runId -cne [string]$plan.runId -or
        [int]$state.processId -le 0) {
        throw 'The canary topology process record is invalid.'
    }
    try {
        $process = [Diagnostics.Process]::GetProcessById(
            [int]$state.processId)
    } catch [ArgumentException] {
        $process = $null
    }
    if ($null -ne $process) {
        $expectedStart = [DateTimeOffset]::Parse(
            [string]$state.startedAt,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor
            [Globalization.DateTimeStyles]::AdjustToUniversal)
        $actualStart = [DateTimeOffset]::new(
            $process.StartTime.ToUniversalTime())
        if ([Math]::Abs(
                ($actualStart - $expectedStart).TotalSeconds) -gt 1) {
            throw 'The canary topology process identity no longer matches.'
        }
        if ($null -ne $containerTopology) {
            Update-ContainerRuntimeRecord `
                -RunRoot $RunRoot `
                -Plan $plan `
                -Topology $containerTopology
        }
        [IO.File]::WriteAllText(
            (Join-Path $RunRoot 'stop.request'),
            [string]$plan.runId,
            [Text.UTF8Encoding]::new($false)
        )
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -ErrorAction Stop
            if (-not $process.WaitForExit(10000)) {
                throw 'The exact canary topology process did not stop.'
            }
        }
    }
}

if ([string]$plan.topologyProfile -ceq 'containerized') {
    $topology = $containerTopology
    $containerRuntimePath = Join-Path $RunRoot 'container-runtime.json'
    if (Test-Path -LiteralPath $containerRuntimePath -PathType Leaf) {
        $runtimeItem = Get-Item -LiteralPath $containerRuntimePath -Force
        if (($runtimeItem.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $runtimeItem.Length -le 0 -or
            $runtimeItem.Length -gt 8192) {
            throw 'The container runtime record exceeds its file contract.'
        }
        $containerRuntime = Get-Content `
            -LiteralPath $containerRuntimePath `
            -Raw `
            -Encoding utf8 |
            ConvertFrom-Json -Depth 10 -ErrorAction Stop
        if ($containerRuntime.schemaVersion -ne 1 -or
            [string]$containerRuntime.runId -cne [string]$plan.runId -or
            [string]$containerRuntime.dashboardContainerId -cnotmatch
                '^[a-f0-9]{64}$' -or
            [string]$containerRuntime.relayContainerId -cnotmatch
                '^[a-f0-9]{64}$') {
            throw 'The container runtime record is invalid.'
        }
        foreach ($container in @(
                [PSCustomObject]@{
                    Id = [string]$containerRuntime.dashboardContainerId
                    ImageId = [string]$topology.dashboardImage.imageId
                    Component = 'dashboard'
                },
                [PSCustomObject]@{
                    Id = [string]$containerRuntime.relayContainerId
                    ImageId = [string]$topology.relayImage.imageId
                    Component = 'relay'
                }
            )) {
            if (Test-SupportCanaryDockerObject `
                    -Kind container `
                    -Identity $container.Id) {
                Assert-SupportCanaryContainerInstance `
                    -ContainerId $container.Id `
                    -ExpectedImageId $container.ImageId `
                    -RunId ([string]$plan.runId) `
                    -Component $container.Component
                Invoke-SupportCanaryNative docker @(
                    'container',
                    'rm',
                    '--force',
                    $container.Id
                )
            }
        }
        foreach ($containerName in @(
                [string]$topology.dashboardContainerName,
                [string]$topology.relayContainerName
            )) {
            if (Test-SupportCanaryDockerObject `
                    -Kind container `
                    -Identity $containerName) {
                throw 'An unrecorded run-scoped canary container remains.'
            }
        }
    } else {
        foreach ($containerName in @(
                [string]$topology.dashboardContainerName,
                [string]$topology.relayContainerName
            )) {
            if (Test-SupportCanaryDockerObject `
                    -Kind container `
                    -Identity $containerName) {
                throw 'Container cleanup requires the exact container-ID record.'
            }
        }
    }
    foreach ($volumeName in @(
            [string]$topology.dashboardVolumeName,
            [string]$topology.relayVolumeName
        )) {
        if (Test-SupportCanaryDockerObject `
                -Kind volume `
                -Identity $volumeName) {
            Invoke-SupportCanaryNative docker @(
                'volume',
                'rm',
                $volumeName
            )
        }
    }
    foreach ($image in @(
            [PSCustomObject]@{
                Identity = $topology.dashboardImage
                Component = 'dashboard'
            },
            [PSCustomObject]@{
                Identity = $topology.relayImage
                Component = 'relay'
            }
        )) {
        if (Test-SupportCanaryDockerObject `
                -Kind image `
                -Identity ([string]$image.Identity.reference)) {
            Assert-SupportCanaryContainerImage `
                -Identity $image.Identity `
                -DashboardCommit ([string]$plan.dashboard.commit) `
                -RunId ([string]$plan.runId) `
                -Component $image.Component
            Invoke-SupportCanaryNative docker @(
                'image',
                'rm',
                [string]$image.Identity.reference
            )
        }
    }
}

if ($hasProcessState) {
    Remove-Item -LiteralPath $processPath -Force
}
