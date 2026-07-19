#Requires -Version 7.0
<#
.SYNOPSIS
    Build and smoke-test the repository's declared container image.

.DESCRIPTION
    Reads the host-specific .container/image.json contract, builds and runs the
    linux/amd64 image, verifies non-root execution and the declared smoke probe,
    and optionally cross-builds linux/arm64 as an OCI archive without emulation.

.PARAMETER ConfigPath
    Repository-relative path to the container image contract.

.PARAMETER Mode
    Smoke builds and runs linux/amd64. Validate also cross-builds linux/arm64 and
    verifies the OCI archive's target platform.

.OUTPUTS
    PSCustomObject with validation status, measurements, and errors.

.EXAMPLE
    ./scripts/container/Test-ContainerImage.ps1 -ConfigPath .container/image.json -Mode Validate
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ConfigPath,

    [Parameter(Mandatory)]
    [ValidateSet('Smoke', 'Validate')]
    [string]$Mode
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$resolvedConfigPath = Join-Path $repositoryRoot $ConfigPath
if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
    Write-Error "Container image contract not found at '$resolvedConfigPath'."
}

$config = Get-Content -LiteralPath $resolvedConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.schemaVersion -ne 1) {
    Write-Error "Unsupported container image contract version '$($config.schemaVersion)'."
}
foreach ($field in @('imageName', 'dockerfile', 'smoke')) {
    if (-not $config.PSObject.Properties[$field]) {
        Write-Error "Container image contract is missing '$field'."
    }
}

$dockerfilePath = Join-Path $repositoryRoot ([string]$config.dockerfile)
if (-not (Test-Path -LiteralPath $dockerfilePath)) {
    Write-Error "Dockerfile not found at '$dockerfilePath'."
}
if ([string]$config.smoke.kind -notin @('http', 'process')) {
    Write-Error "Unsupported container smoke kind '$($config.smoke.kind)'."
}
if ([string]$config.smoke.kind -eq 'http') {
    if (-not $config.smoke.PSObject.Properties['containerPort'] -or
        -not $config.smoke.PSObject.Properties['healthPath']) {
        Write-Error "HTTP smoke contracts require containerPort and healthPath."
    }
}
$runEnvironment = [ordered]@{}
if ($config.PSObject.Properties['runEnvironment']) {
    if ($config.runEnvironment -isnot [System.Management.Automation.PSCustomObject]) {
        Write-Error 'Container runEnvironment must be an object of string values.'
    }
    foreach ($property in $config.runEnvironment.PSObject.Properties) {
        $value = [string]$property.Value
        if ([string]::IsNullOrWhiteSpace($property.Name) -or $value -match '[\r\n]') {
            Write-Error "Container runEnvironment entry '$($property.Name)' is invalid."
        }
        $runEnvironment[$property.Name] = $value
    }
}

$errors = [System.Collections.Generic.List[string]]::new()
$runId = [guid]::NewGuid().ToString('N').Substring(0, 10)
$imageSlug = ([string]$config.imageName).ToLowerInvariant() -replace '[^a-z0-9._-]+', '-'
$imageSlug = $imageSlug.Trim('-', '.')
if ([string]::IsNullOrWhiteSpace($imageSlug)) {
    Write-Error "Container image name '$($config.imageName)' cannot be normalized."
}

$imageTag = "${imageSlug}:genesis-$runId"
$containerName = "genesis-container-$runId"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "genesis-container-$runId"
$filesystemArchive = Join-Path $tempRoot 'filesystem.tar'
$arm64Archive = Join-Path $tempRoot 'arm64.tar'
$arm64Extract = Join-Path $tempRoot 'arm64'
$containerStarted = $false
$amd64ImageBytes = 0L
$arm64ArchiveBytes = 0L
$runtimeUser = ''
$dockerAvailable = $false
$temporaryBuilder = ''
$requiresImageHealthCheck =
    -not $config.PSObject.Properties['requireImageHealthCheck'] -or
    [bool]$config.requireImageHealthCheck

function Invoke-Docker {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [switch]$Capture
    )

    $output = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed:`n$($output | Out-String)"
    }
    if ($Capture) {
        return @($output)
    }
    $output | ForEach-Object { Write-Host $_ }
}

function Test-ContainerRunning {
    $running = @(Invoke-Docker -Arguments @(
        'inspect',
        '--format',
        '{{.State.Running}}',
        $containerName
    ) -Capture)
    return ([string]$running[-1]).Trim() -eq 'true'
}

function Wait-ForHttpProbe {
    param(
        [Parameter(Mandatory)]
        [int]$HostPort,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $uri = "http://127.0.0.1:$HostPort$Path"
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (-not (Test-ContainerRunning)) {
            throw "Container exited before '$uri' became healthy."
        }

        try {
            $response = Invoke-WebRequest -Uri $uri -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                return $response
            }
        } catch {
            if ($attempt -eq 30) {
                throw "HTTP probe '$uri' did not succeed within 30 seconds: $($_.Exception.Message)"
            }
        }
        Start-Sleep -Seconds 1
    }
}

function Wait-ForContainerHealth {
    for ($attempt = 1; $attempt -le 45; $attempt++) {
        if (-not (Test-ContainerRunning)) {
            throw 'Container exited before its image health check became healthy.'
        }

        $healthStatus = @(Invoke-Docker -Arguments @(
            'inspect',
            '--format',
            '{{.State.Health.Status}}',
            $containerName
        ) -Capture)
        $status = ([string]$healthStatus[-1]).Trim()
        if ($status -eq 'healthy') {
            return
        }
        if ($status -eq 'unhealthy') {
            throw 'Container image health check reported unhealthy.'
        }
        Start-Sleep -Seconds 1
    }
    throw 'Container image health check did not become healthy within 45 seconds.'
}

function Get-OciBlobPath {
    param(
        [Parameter(Mandatory)]
        [string]$Digest
    )

    $parts = $Digest.Split(':', 2)
    if ($parts.Count -ne 2) {
        throw "Invalid OCI digest '$Digest'."
    }
    return Join-Path (Join-Path $arm64Extract 'blobs') (Join-Path $parts[0] $parts[1])
}

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is required for container image validation.'
    }
    $dockerAvailable = $true

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Push-Location $repositoryRoot
    try {
        $temporaryBuilder = "genesis-container-$runId"
        Invoke-Docker -Arguments @(
            'buildx',
            'create',
            '--name',
            $temporaryBuilder,
            '--driver',
            'docker-container'
        )
        Invoke-Docker -Arguments @('buildx', 'inspect', $temporaryBuilder, '--bootstrap')
        Invoke-Docker -Arguments @(
            'buildx',
            'build',
            '--builder',
            $temporaryBuilder,
            '--platform',
            'linux/amd64',
            '--load',
            '--tag',
            $imageTag,
            '--file',
            ([string]$config.dockerfile),
            '.'
        )

        $architecture = @(Invoke-Docker -Arguments @(
            'image',
            'inspect',
            '--format',
            '{{.Architecture}}',
            $imageTag
        ) -Capture)
        if (([string]$architecture[-1]).Trim() -ne 'amd64') {
            throw "Loaded image architecture was '$architecture', expected amd64."
        }

        $runtimeUserOutput = @(Invoke-Docker -Arguments @(
            'image',
            'inspect',
            '--format',
            '{{.Config.User}}',
            $imageTag
        ) -Capture)
        $runtimeUser = ([string]$runtimeUserOutput[-1]).Trim()
        if ([string]::IsNullOrWhiteSpace($runtimeUser) -or $runtimeUser -in @('0', 'root')) {
            throw "Image runtime user '$runtimeUser' is not a non-root user."
        }

        if ($requiresImageHealthCheck) {
            $healthConfig = @(Invoke-Docker -Arguments @(
                'image',
                'inspect',
                '--format',
                '{{json .Config.Healthcheck}}',
                $imageTag
            ) -Capture)
            if (([string]$healthConfig[-1]).Trim() -in @('', 'null', '<nil>')) {
                throw 'Image does not declare a health check.'
            }
        }

        $runArguments = [System.Collections.Generic.List[string]]::new()
        foreach ($argument in @('run', '--detach', '--name', $containerName)) {
            $runArguments.Add($argument)
        }
        if ([string]$config.smoke.kind -eq 'http') {
            $runArguments.Add('--publish')
            $runArguments.Add("127.0.0.1::$($config.smoke.containerPort)")
        }
        foreach ($entry in $runEnvironment.GetEnumerator()) {
            $runArguments.Add('--env')
            $runArguments.Add("$($entry.Key)=$($entry.Value)")
        }
        $runArguments.Add($imageTag)
        Invoke-Docker -Arguments ([string[]]$runArguments)
        $containerStarted = $true

        if ([string]$config.smoke.kind -eq 'http') {
            $portOutput = @(Invoke-Docker -Arguments @(
                'port',
                $containerName,
                "$($config.smoke.containerPort)/tcp"
            ) -Capture)
            $portMatch = [regex]::Match([string]$portOutput[-1], ':(?<port>\d+)$')
            if (-not $portMatch.Success) {
                throw "Could not resolve the published host port from '$($portOutput[-1])'."
            }

            $hostPort = [int]$portMatch.Groups['port'].Value
            Wait-ForHttpProbe -HostPort $hostPort -Path ([string]$config.smoke.healthPath) | Out-Null
            if ($config.smoke.PSObject.Properties['rootPath']) {
                $rootResponse = Wait-ForHttpProbe `
                    -HostPort $hostPort `
                    -Path ([string]$config.smoke.rootPath)
                if ($rootResponse.Content -notmatch '(?i)<html') {
                    throw "Root probe '$($config.smoke.rootPath)' did not return HTML."
                }
            }
        } else {
            $startupSeconds = if ($config.smoke.PSObject.Properties['startupSeconds']) {
                [int]$config.smoke.startupSeconds
            } else {
                5
            }
            Start-Sleep -Seconds $startupSeconds
            if (-not (Test-ContainerRunning)) {
                throw "Container did not remain running for $startupSeconds seconds."
            }
        }

        if ($requiresImageHealthCheck) {
            Wait-ForContainerHealth
        }

        Invoke-Docker -Arguments @('export', '--output', $filesystemArchive, $containerName)
        $filesystemEntries = & tar -tf $filesystemArchive 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect container filesystem:`n$($filesystemEntries | Out-String)"
        }
        $forbiddenRuntimePaths = @(
            '^usr/share/dotnet/sdk/',
            '^usr/local/bin/node$',
            '^usr/bin/node$',
            '^usr/local/lib/node_modules/'
        )
        foreach ($pattern in $forbiddenRuntimePaths) {
            if ($filesystemEntries | Where-Object { $_ -match $pattern }) {
                throw "Final image contains forbidden build-tool path matching '$pattern'."
            }
        }

        $sizeOutput = @(Invoke-Docker -Arguments @(
            'image',
            'inspect',
            '--format',
            '{{.Size}}',
            $imageTag
        ) -Capture)
        $amd64ImageBytes = [long]([string]$sizeOutput[-1]).Trim()

        if ($Mode -eq 'Validate') {
            Invoke-Docker -Arguments @(
                'buildx',
                'build',
                '--builder',
                $temporaryBuilder,
                '--platform',
                'linux/arm64',
                '--output',
                "type=oci,dest=$arm64Archive",
                '--file',
                ([string]$config.dockerfile),
                '.'
            )

            New-Item -ItemType Directory -Path $arm64Extract -Force | Out-Null
            $extractOutput = & tar -xf $arm64Archive -C $arm64Extract 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "Could not extract arm64 OCI archive:`n$($extractOutput | Out-String)"
            }

            $index = Get-Content (Join-Path $arm64Extract 'index.json') -Raw | ConvertFrom-Json
            $descriptor = @($index.manifests | Where-Object {
                $_.platform.architecture -eq 'arm64' -and $_.platform.os -eq 'linux'
            }) | Select-Object -First 1
            if (-not $descriptor) {
                throw 'arm64 OCI archive has no linux/arm64 manifest.'
            }

            $manifest = Get-Content (Get-OciBlobPath -Digest $descriptor.digest) -Raw | ConvertFrom-Json
            $imageConfig = Get-Content (Get-OciBlobPath -Digest $manifest.config.digest) -Raw | ConvertFrom-Json
            if ($imageConfig.architecture -ne 'arm64' -or $imageConfig.os -ne 'linux') {
                throw "OCI image config is '$($imageConfig.os)/$($imageConfig.architecture)', expected linux/arm64."
            }
            $arm64ArchiveBytes = (Get-Item -LiteralPath $arm64Archive).Length
        }
    } finally {
        Pop-Location
    }
} catch {
    $errors.Add($_.Exception.Message)
    if ($containerStarted) {
        try {
            Invoke-Docker -Arguments @('logs', $containerName)
        } catch {
            Write-Warning $_.Exception.Message
        }
    }
} finally {
    if ($dockerAvailable) {
        if ($containerStarted) {
            & docker rm --force $containerName *> $null
        }
        & docker image rm --force $imageTag *> $null
        if ($temporaryBuilder) {
            & docker buildx rm --force $temporaryBuilder *> $null
        }
    }
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

[PSCustomObject]@{
    IsClean           = ($errors.Count -eq 0)
    Errors            = @($errors)
    Amd64ImageBytes   = $amd64ImageBytes
    Arm64ArchiveBytes = $arm64ArchiveBytes
    RuntimeUser       = $runtimeUser
}
