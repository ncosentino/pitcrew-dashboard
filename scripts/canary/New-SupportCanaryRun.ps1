#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$DashboardCommit,

    [Parameter(Mandatory)]
    [string]$PitCrewSourceRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$PitCrewCommit,

    [Parameter(Mandatory)]
    [string]$OutputRoot,

    [ValidateSet(
        'topology-smoke-v1',
        'support-fresh-enrollment-diagnostic-v1')]
    [string[]]$Scenario = @(
        'support-fresh-enrollment-diagnostic-v1'),

    [ValidateSet('portable', 'windows-installed')]
    [string]$TopologyProfile = 'portable'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

Assert-SupportCanaryCommit `
    -SourceRoot $DashboardSourceRoot `
    -ExpectedCommit $DashboardCommit
Assert-SupportCanaryCommit `
    -SourceRoot $PitCrewSourceRoot `
    -ExpectedCommit $PitCrewCommit

$runId = [Guid]::NewGuid().ToString('N')
$output = [IO.Path]::GetFullPath($OutputRoot)
$runRoot = Join-Path $output $runId
if (Test-Path -LiteralPath $runRoot) {
    throw 'The generated canary run root already exists.'
}
$fixtureRoot = Join-Path $runRoot 'fixture/pitcrew'
$evidenceRoot = Join-Path $runRoot 'evidence'
$servicesRoot = Join-Path $runRoot 'services'
$null = New-Item -ItemType Directory -Path $fixtureRoot
$null = New-Item -ItemType Directory -Path $evidenceRoot
$null = New-Item -ItemType Directory -Path $servicesRoot

foreach ($sentinel in @(
        'Setup-Runner.ps1',
        'RunnerProfiles.Functions.ps1',
        'docker-compose.yml'
    )) {
    Copy-Item `
        -LiteralPath (Join-Path $PitCrewSourceRoot $sentinel) `
        -Destination (Join-Path $fixtureRoot $sentinel)
}
$candidateScripts = Join-Path `
    $PitCrewSourceRoot `
    'plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts'
$fixtureScripts = Join-Path `
    $fixtureRoot `
    'plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts'
$null = New-Item -ItemType Directory -Path $fixtureScripts
Copy-Item `
    -Path (Join-Path $candidateScripts '*') `
    -Destination $fixtureScripts `
    -Recurse
$supportEvidenceRoot = New-Item `
    -ItemType Directory `
    -Path (Join-Path (
        Join-Path $fixtureRoot '.pitcrew-state/default'
    ) 'support-evidence')
$observedAt = [DateTimeOffset]::UtcNow.ToString('O')
$imageId = 'sha256:' + [string]::new('d', 64)
$projections = [ordered]@{
    'desired-capacity.json' = [ordered]@{
        schemaVersion = 1
        generation = 1
        scope = 'repo'
        repositories = @(
            [ordered]@{
                url = 'https://github.com/example/project'
                workers = 1
            })
        replicas = $null
    }
    'acknowledged-capacity.json' = [ordered]@{
        schemaVersion = 1
        status = 'accepted'
        generation = 1
        managerContractVersion = 18
        desiredStateHash = [string]::new('a', 64)
        observedAt = $observedAt
        desiredSlots = 1
        addedSlots = 0
        drainingSlots = 0
        unchangedSlots = 1
    }
    'static-profile.json' = [ordered]@{
        schemaVersion = 1
        fingerprint = [string]::new('b', 64)
        workerRevision = [string]::new('c', 64)
        manifest = $null
        configuration = [ordered]@{
            managerContractVersion = 18
            workerRuntimeContractVersion = 3
            profile = 'default'
            image = 'ghcr.io/example/worker:1.0.0'
            resolvedImageId = $imageId
            pullImage = $true
            scope = 'repo'
            autoscaling = $null
            resources = [ordered]@{
                memoryBytes = 2147483648
                memorySwapBytes = 4294967296
                cpuCores = '2'
                pids = 1024
            }
            runtime = [ordered]@{
                devices = @()
                sharedMemoryBytes = 67108864
            }
        }
    }
    'observed-state.json' = [ordered]@{
        schemaVersion = 1
        managerContractVersion = 18
        profileId = 'default'
        managerStatus = 'running'
        observedAt = $observedAt
        scope = 'repo'
        generation = 1
        desiredStateStatus = 'accepted'
        desiredSlots = 1
        configuredSlots = 1
        activeSlots = 0
        eligibleSlots = 1
        drainingSlots = 0
        slots = @(
            [ordered]@{
                key = 'slot-1'
                repository = 'https://github.com/example/project'
                desired = $true
                processRunning = $true
                state = 'online'
                activity = 'idle'
                registrationStatus = 'connected'
                target = 'https://github.com/example/project'
                currentJob = $null
            })
        autoscaling = $null
        update = [ordered]@{
            status = 'current'
            targetImage = 'ghcr.io/example/worker:1.0.0'
            targetImageId = $imageId
            targetRevision = [string]::new('c', 64)
            currentWorkers = 1
            staleWorkers = 0
            lastError = $null
        }
        resourceTelemetry = $null
        hostAdmission = $null
        capacityEvidence = [ordered]@{
            fixed = $null
            targets = @()
        }
    }
}
foreach ($projection in $projections.GetEnumerator()) {
    Write-SupportCanaryJson `
        -LiteralPath (Join-Path $supportEvidenceRoot $projection.Key) `
        -Value $projection.Value
}

$plan = [PSCustomObject][ordered]@{
    schemaVersion = 1
    runId = $runId
    topologyProfile = $TopologyProfile
    scenarios = @($Scenario)
    dashboard = [PSCustomObject][ordered]@{
        repository = 'ncosentino/pitcrew-dashboard'
        commit = $DashboardCommit
    }
    pitCrew = [PSCustomObject][ordered]@{
        repository = 'ncosentino/pitcrew'
        commit = $PitCrewCommit
    }
    createdAt = [DateTimeOffset]::UtcNow.ToString('O')
}
Write-SupportCanaryJson `
    -LiteralPath (Join-Path $runRoot 'plan.json') `
    -Value $plan

(Resolve-Path -LiteralPath $runRoot).Path
