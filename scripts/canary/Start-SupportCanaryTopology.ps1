#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [Parameter(Mandatory)]
    [string]$DashboardSourceRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SupportCanary.Common.ps1')

$plan = Read-SupportCanaryPlan -RunRoot $RunRoot
Assert-SupportCanaryCommit `
    -SourceRoot $DashboardSourceRoot `
    -ExpectedCommit ([string]$plan.dashboard.commit)
$runtimePath = Join-Path $RunRoot 'runtime.json'
$processPath = Join-Path $RunRoot 'topology-process.json'
$containerRuntimePath = Join-Path $RunRoot 'container-runtime.json'
if ((Test-Path -LiteralPath $runtimePath) -or
    (Test-Path -LiteralPath $processPath) -or
    (Test-Path -LiteralPath $containerRuntimePath)) {
    throw 'The canary topology already has runtime or process state.'
}
$containerTopology = $null
if ([string]$plan.topologyProfile -ceq 'containerized') {
    $containerTopology = Read-SupportCanaryContainerTopology `
        -RunRoot $RunRoot
    Assert-SupportCanaryContainerImage `
        -Identity $containerTopology.dashboardImage `
        -DashboardCommit ([string]$plan.dashboard.commit) `
        -RunId ([string]$plan.runId) `
        -Component dashboard
    Assert-SupportCanaryContainerImage `
        -Identity $containerTopology.relayImage `
        -DashboardCommit ([string]$plan.dashboard.commit) `
        -RunId ([string]$plan.runId) `
        -Component relay
    foreach ($containerName in @(
            [string]$containerTopology.dashboardContainerName,
            [string]$containerTopology.relayContainerName
        )) {
        if (Test-SupportCanaryDockerObject `
                -Kind container `
                -Identity $containerName) {
            throw 'A run-scoped canary container already exists.'
        }
    }
    foreach ($volumeName in @(
            [string]$containerTopology.dashboardVolumeName,
            [string]$containerTopology.relayVolumeName
        )) {
        if (Test-SupportCanaryDockerObject `
                -Kind volume `
                -Identity $volumeName) {
            throw 'A run-scoped canary volume already exists.'
        }
    }
}
$appHostAssembly = Get-SupportCanaryProjectAssembly `
    -DashboardSourceRoot $DashboardSourceRoot `
    -ProjectName 'PitCrew.Support.Canary.AppHost' `
    -Configuration $Configuration
$secretBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$relaySecret = [Convert]::ToBase64String($secretBytes)
[Security.Cryptography.CryptographicOperations]::ZeroMemory($secretBytes)
$curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName(
    'nistP256')
$ecdsa = [Security.Cryptography.ECDsa]::Create($curve)
$rsa = [Security.Cryptography.RSA]::Create(3072)
try {
    $authorizationBytes = $ecdsa.ExportPkcs8PrivateKey()
    $resultBytes = $rsa.ExportPkcs8PrivateKey()
    try {
        $dashboardAuthorizationKey = (
            [Convert]::ToBase64String($authorizationBytes)
        ).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        $dashboardResultKey = (
            [Convert]::ToBase64String($resultBytes)
        ).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    } finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            $authorizationBytes)
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            $resultBytes)
    }
} finally {
    $ecdsa.Dispose()
    $rsa.Dispose()
}

$priorEnvironment = @{}
foreach ($name in @(
        'PITCREW_CANARY_RUN_ROOT',
        'PITCREW_CANARY_RUN_ID',
        'PITCREW_CANARY_DASHBOARD_SOURCE_ROOT',
        'PITCREW_CANARY_DOTNET_CONFIGURATION',
        'Parameters__relay-secret',
        'Parameters__dashboard-authorization-key',
        'Parameters__dashboard-result-key'
    )) {
    $priorEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_RUN_ROOT',
        [IO.Path]::GetFullPath($RunRoot))
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_RUN_ID',
        [string]$plan.runId)
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_DASHBOARD_SOURCE_ROOT',
        [IO.Path]::GetFullPath($DashboardSourceRoot))
    [Environment]::SetEnvironmentVariable(
        'PITCREW_CANARY_DOTNET_CONFIGURATION',
        $Configuration)
    [Environment]::SetEnvironmentVariable(
        'Parameters__relay-secret',
        $relaySecret)
    [Environment]::SetEnvironmentVariable(
        'Parameters__dashboard-authorization-key',
        $dashboardAuthorizationKey)
    [Environment]::SetEnvironmentVariable(
        'Parameters__dashboard-result-key',
        $dashboardResultKey)
    $process = Start-Process `
        -FilePath dotnet `
        -ArgumentList @($appHostAssembly) `
        -WorkingDirectory $RunRoot `
        -RedirectStandardOutput (Join-Path $RunRoot 'topology.stdout.log') `
        -RedirectStandardError (Join-Path $RunRoot 'topology.stderr.log') `
        -PassThru
} finally {
    foreach ($pair in $priorEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            [string]$pair.Key,
            $pair.Value)
    }
    $relaySecret = $null
    $dashboardAuthorizationKey = $null
    $dashboardResultKey = $null
}

$processState = [PSCustomObject][ordered]@{
    schemaVersion = 1
    runId = [string]$plan.runId
    processId = $process.Id
    startedAt = $process.StartTime.ToUniversalTime().ToString('O')
}
Write-SupportCanaryJson `
    -LiteralPath $processPath `
    -Value $processState

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    if ($process.HasExited) {
        throw 'The canary topology exited before becoming ready.'
    }
    if (Test-Path -LiteralPath $runtimePath -PathType Leaf) {
        try {
            $runtime = Get-Content `
                -LiteralPath $runtimePath `
                -Raw `
                -Encoding utf8 |
                ConvertFrom-Json -Depth 20
            if ($null -ne $containerTopology) {
                $dashboardContainerId = [string](
                    & docker container inspect `
                        --format '{{.Id}}' `
                        ([string]$containerTopology.dashboardContainerName)
                )
                $dashboardContainerId = $dashboardContainerId.Trim()
                $relayContainerId = [string](
                    & docker container inspect `
                        --format '{{.Id}}' `
                        ([string]$containerTopology.relayContainerName)
                )
                $relayContainerId = $relayContainerId.Trim()
                if ($LASTEXITCODE -ne 0 -or
                    $dashboardContainerId -cnotmatch '^[a-f0-9]{64}$' -or
                    $relayContainerId -cnotmatch '^[a-f0-9]{64}$') {
                    throw 'The active canary container identity is invalid.'
                }
                Assert-SupportCanaryContainerInstance `
                    -ContainerId $dashboardContainerId `
                    -ExpectedImageId (
                        [string]$containerTopology.dashboardImage.imageId
                    ) `
                    -RunId ([string]$plan.runId) `
                    -Component dashboard
                Assert-SupportCanaryContainerInstance `
                    -ContainerId $relayContainerId `
                    -ExpectedImageId (
                        [string]$containerTopology.relayImage.imageId
                    ) `
                    -RunId ([string]$plan.runId) `
                    -Component relay
                $dashboardNetworks = @(
                    Assert-SupportCanaryContainerIsolation `
                        -ContainerId $dashboardContainerId `
                        -ExpectedContainerName (
                            [string]$containerTopology.dashboardContainerName
                        ) `
                        -ExpectedVolumeName (
                            [string]$containerTopology.dashboardVolumeName
                        ) `
                        -ExpectedVolumeTarget '/var/lib/pitcrew-dashboard' `
                        -ExpectedTmpfsSize '512m'
                )
                $relayNetworks = @(
                    Assert-SupportCanaryContainerIsolation `
                        -ContainerId $relayContainerId `
                        -ExpectedContainerName (
                            [string]$containerTopology.relayContainerName
                        ) `
                        -ExpectedVolumeName (
                            [string]$containerTopology.relayVolumeName
                        ) `
                        -ExpectedVolumeTarget '/var/lib/pitcrew-support-relay' `
                        -ExpectedTmpfsSize '64m' `
                        -RequiredNetworkAlias 'support-relay-internal'
                )
                if (@(
                        $dashboardNetworks |
                            Where-Object {
                                $relayNetworks -ccontains $_
                            }
                    ).Count -ne 1) {
                    throw (
                        'The canary containers do not share one session ' +
                        'network.'
                    )
                }
                $containerRuntime = [PSCustomObject][ordered]@{
                    schemaVersion = 1
                    runId = [string]$plan.runId
                    dashboardContainerId = $dashboardContainerId
                    relayContainerId = $relayContainerId
                }
                Write-SupportCanaryJson `
                    -LiteralPath $containerRuntimePath `
                    -Value $containerRuntime
            }
            return $runtime
        } catch {
            if (Test-Path -LiteralPath $runtimePath -PathType Leaf) {
                Remove-Item -LiteralPath $runtimePath -Force
            }
            throw
        }
    }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)

throw 'The canary topology did not become ready within its timeout.'
