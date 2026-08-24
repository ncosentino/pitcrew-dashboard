Set-StrictMode -Version Latest

function Assert-SupportCanaryCommit {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRoot,

        [Parameter(Mandatory)]
        [ValidatePattern('^[a-f0-9]{40}$')]
        [string]$ExpectedCommit
    )

    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
        throw 'The canary source root is unavailable.'
    }
    $actual = & git -C $SourceRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($actual) -or
        $actual.Trim() -cne $ExpectedCommit) {
        throw 'The canary source root does not match its immutable commit.'
    }
    $changes = & git -C $SourceRoot status --porcelain
    if ($LASTEXITCODE -ne 0 -or @($changes).Count -ne 0) {
        throw 'The canary source root contains uncommitted changes.'
    }
}

function Read-SupportCanaryPlan {
    param(
        [Parameter(Mandatory)]
        [string]$RunRoot
    )

    $planPath = Join-Path $RunRoot 'plan.json'
    if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
        throw 'The canary plan manifest is unavailable.'
    }
    $item = Get-Item -LiteralPath $planPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -le 0 -or
        $item.Length -gt 65536) {
        throw 'The canary plan manifest exceeds its file contract.'
    }
    $plan = Get-Content -LiteralPath $planPath -Raw -Encoding utf8 |
        ConvertFrom-Json -Depth 20 -ErrorAction Stop
    if ($plan.schemaVersion -ne 1 -or
        [string]$plan.runId -notmatch '^[a-f0-9]{32}$' -or
        [string]$plan.topologyProfile -notin @(
            'portable',
            'containerized',
            'windows-installed'
        ) -or
        [string]$plan.dashboard.commit -notmatch '^[a-f0-9]{40}$' -or
        [string]$plan.pitCrew.commit -notmatch '^[a-f0-9]{40}$') {
        throw 'The canary plan manifest is invalid.'
    }
    return $plan
}

function Read-SupportCanaryContainerTopology {
    param(
        [Parameter(Mandatory)]
        [string]$RunRoot
    )

    $plan = Read-SupportCanaryPlan -RunRoot $RunRoot
    if ([string]$plan.topologyProfile -cne 'containerized') {
        throw 'The canary plan does not select the containerized profile.'
    }
    $path = Join-Path $RunRoot 'container-topology.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'The container topology manifest is unavailable.'
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -le 0 -or
        $item.Length -gt 16384) {
        throw 'The container topology manifest exceeds its file contract.'
    }
    $topology = Get-Content -LiteralPath $path -Raw -Encoding utf8 |
        ConvertFrom-Json -Depth 20 -ErrorAction Stop
    $runId = [string]$plan.runId
    if ($topology.schemaVersion -ne 1 -or
        [string]$topology.runId -cne $runId -or
        [string]$topology.dashboard.repository -cne
            'ncosentino/pitcrew-dashboard' -or
        [string]$topology.dashboard.commit -cne
            [string]$plan.dashboard.commit -or
        [string]$topology.dashboardImage.reference -cne
            "pitcrew-support-canary-dashboard:$runId" -or
        [string]$topology.relayImage.reference -cne
            "pitcrew-support-canary-relay:$runId" -or
        [string]$topology.dashboardImage.imageId -cnotmatch
            '^sha256:[a-f0-9]{64}$' -or
        [string]$topology.relayImage.imageId -cnotmatch
            '^sha256:[a-f0-9]{64}$' -or
        [string]$topology.dashboardContainerName -cne
            "pitcrew-canary-$runId-dashboard" -or
        [string]$topology.relayContainerName -cne
            "pitcrew-canary-$runId-relay" -or
        [string]$topology.dashboardVolumeName -cne
            "pitcrew-canary-$runId-dashboard-data" -or
        [string]$topology.relayVolumeName -cne
            "pitcrew-canary-$runId-relay-data") {
        throw 'The container topology manifest is invalid.'
    }
    return $topology
}

function Test-SupportCanaryDockerObject {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('container', 'image', 'volume')]
        [string]$Kind,

        [Parameter(Mandatory)]
        [string]$Identity
    )

    # A nonzero inspect result deliberately means the exact object is absent.
    $null = & docker $Kind inspect $Identity 2>&1
    if ($LASTEXITCODE -eq 0) {
        return $true
    }
    $null = & docker info --format '{{.OSType}}' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'The container engine is unavailable during identity inspection.'
    }
    return $false
}

function Assert-SupportCanaryContainerImage {
    param(
        [Parameter(Mandatory)]
        [object]$Identity,

        [Parameter(Mandatory)]
        [ValidatePattern('^[a-f0-9]{40}$')]
        [string]$DashboardCommit,

        [Parameter(Mandatory)]
        [ValidatePattern('^[a-f0-9]{32}$')]
        [string]$RunId,

        [Parameter(Mandatory)]
        [ValidateSet('dashboard', 'relay')]
        [string]$Component
    )

    $reference = [string]$Identity.reference
    $imageId = [string](& docker image inspect `
        --format '{{.Id}}' `
        $reference)
    $imageId = $imageId.Trim()
    if ($LASTEXITCODE -ne 0 -or
        $imageId -cne [string]$Identity.imageId) {
        throw 'The candidate container image identity changed.'
    }
    $labelsJson = [string](& docker image inspect `
        --format '{{json .Config.Labels}}' `
        $reference)
    $labelsJson = $labelsJson.Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($labelsJson)) {
        throw 'The candidate container image labels are unavailable.'
    }
    $labels = $labelsJson | ConvertFrom-Json -Depth 10 -ErrorAction Stop
    if ([string]$labels.'io.pitcrew.canary.run-id' -cne $RunId -or
        [string]$labels.'io.pitcrew.canary.component' -cne $Component -or
        [string]$labels.'org.opencontainers.image.revision' -cne
            $DashboardCommit) {
        throw 'The candidate container image labels do not match the run.'
    }
}

function Assert-SupportCanaryContainerInstance {
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^[a-f0-9]{64}$')]
        [string]$ContainerId,

        [Parameter(Mandatory)]
        [ValidatePattern('^sha256:[a-f0-9]{64}$')]
        [string]$ExpectedImageId,

        [Parameter(Mandatory)]
        [ValidatePattern('^[a-f0-9]{32}$')]
        [string]$RunId,

        [Parameter(Mandatory)]
        [ValidateSet('dashboard', 'relay')]
        [string]$Component
    )

    $imageId = [string](& docker container inspect `
        --format '{{.Image}}' `
        $ContainerId)
    $imageId = $imageId.Trim()
    $labelsJson = [string](& docker container inspect `
        --format '{{json .Config.Labels}}' `
        $ContainerId)
    $labelsJson = $labelsJson.Trim()
    if ($LASTEXITCODE -ne 0 -or
        $imageId -cne $ExpectedImageId -or
        [string]::IsNullOrWhiteSpace($labelsJson)) {
        throw 'The active canary container identity changed.'
    }
    $labels = $labelsJson | ConvertFrom-Json -Depth 10 -ErrorAction Stop
    if ([string]$labels.'io.pitcrew.canary.run-id' -cne $RunId -or
        [string]$labels.'io.pitcrew.canary.component' -cne $Component) {
        throw 'The active canary container labels do not match the run.'
    }
}

function Assert-SupportCanaryContainerIsolation {
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^[a-f0-9]{64}$')]
        [string]$ContainerId,

        [Parameter(Mandatory)]
        [string]$ExpectedContainerName,

        [Parameter(Mandatory)]
        [string]$ExpectedVolumeName,

        [Parameter(Mandatory)]
        [string]$ExpectedVolumeTarget,

        [Parameter(Mandatory)]
        [ValidateSet('64m', '512m')]
        [string]$ExpectedTmpfsSize,

        [string]$RequiredNetworkAlias = ''
    )

    $containerName = [string](& docker container inspect `
        --format '{{.Name}}' `
        $ContainerId)
    $hostConfigJson = [string](& docker container inspect `
        --format '{{json .HostConfig}}' `
        $ContainerId)
    $mountsJson = [string](& docker container inspect `
        --format '{{json .Mounts}}' `
        $ContainerId)
    $networksJson = [string](& docker container inspect `
        --format '{{json .NetworkSettings.Networks}}' `
        $ContainerId)
    if ($LASTEXITCODE -ne 0 -or
        $containerName.Trim() -cne "/$ExpectedContainerName" -or
        [string]::IsNullOrWhiteSpace($hostConfigJson) -or
        [string]::IsNullOrWhiteSpace($mountsJson) -or
        [string]::IsNullOrWhiteSpace($networksJson)) {
        throw 'The canary container isolation evidence is unavailable.'
    }
    $hostConfig = $hostConfigJson |
        ConvertFrom-Json -Depth 20 -ErrorAction Stop
    $mounts = @(
        $mountsJson | ConvertFrom-Json -Depth 20 -ErrorAction Stop
    )
    $networks = $networksJson |
        ConvertFrom-Json -Depth 20 -ErrorAction Stop
    $tmpfs = [string]$hostConfig.Tmpfs.'/tmp'
    $securityOptions = @($hostConfig.SecurityOpt)
    $expectedTmpfsBytes = if ($ExpectedTmpfsSize -ceq '64m') {
        '67108864'
    } else {
        '536870912'
    }
    if (-not [bool]$hostConfig.ReadonlyRootfs -or
        @($hostConfig.CapDrop) -cnotcontains 'ALL' -or
        ($securityOptions -cnotcontains 'no-new-privileges:true' -and
            $securityOptions -cnotcontains 'no-new-privileges') -or
        $tmpfs -notmatch '(^|,)noexec(,|$)' -or
        $tmpfs -notmatch '(^|,)nosuid(,|$)' -or
        $tmpfs -notmatch '(^|,)nodev(,|$)' -or
        $tmpfs -notmatch (
            '(^|,)size=(' + [Regex]::Escape($ExpectedTmpfsSize) +
            '|' + $expectedTmpfsBytes + ')(,|$)'
        )) {
        throw 'The active canary container hardening contract is incomplete.'
    }
    $matchingMounts = @(
        $mounts | Where-Object {
            [string]$_.Type -ceq 'volume' -and
            [string]$_.Name -ceq $ExpectedVolumeName -and
            [string]$_.Destination -ceq $ExpectedVolumeTarget -and
            [bool]$_.RW
        }
    )
    if ($matchingMounts.Count -ne 1) {
        throw 'The active canary container volume contract is invalid.'
    }
    $networkProperties = @($networks.PSObject.Properties)
    if ($networkProperties.Count -lt 1 -or
        $networkProperties.Name -ccontains 'host') {
        throw 'The active canary container network contract is invalid.'
    }
    if (-not [string]::IsNullOrWhiteSpace($RequiredNetworkAlias)) {
        $aliasMatches = @(
            $networkProperties | Where-Object {
                @($_.Value.Aliases) -ccontains $RequiredNetworkAlias
            }
        )
        if ($aliasMatches.Count -ne 1) {
            throw 'The active canary container network alias is unavailable.'
        }
    }
    return @($networkProperties.Name)
}

function Invoke-SupportCanaryNative {
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
        throw "The canary command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SupportCanaryNativeBounded {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ArgumentList,

        [ValidateRange(30, 1800)]
        [int]$TimeoutSeconds = 900
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $startInfo.UseShellExecute = $false
    foreach ($argument in $ArgumentList) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "The canary command '$FilePath' did not start."
        }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) {
                throw "The timed-out canary command '$FilePath' did not stop."
            }
            throw "The canary command '$FilePath' exceeded its timeout."
        }
        if ($process.ExitCode -ne 0) {
            throw (
                "The canary command '$FilePath' failed with exit code " +
                "$($process.ExitCode)."
            )
        }
    } finally {
        $process.Dispose()
    }
}

function Get-SupportCanaryProjectAssembly {
    param(
        [Parameter(Mandatory)]
        [string]$DashboardSourceRoot,

        [Parameter(Mandatory)]
        [string]$ProjectName,

        [Parameter(Mandatory)]
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration
    )

    $path = Join-Path `
        $DashboardSourceRoot `
        "src/$ProjectName/bin/$Configuration/net10.0/$ProjectName.dll"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The candidate assembly '$ProjectName' is unavailable."
    }
    return (Resolve-Path -LiteralPath $path).Path
}

function Write-SupportCanaryJson {
    param(
        [Parameter(Mandatory)]
        [string]$LiteralPath,

        [Parameter(Mandatory)]
        [object]$Value,

        [switch]$Overwrite
    )

    $temporaryPath = "$LiteralPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            (($Value | ConvertTo-Json -Depth 20) + "`n"),
            [Text.UTF8Encoding]::new($false)
        )
        [IO.File]::Move(
            $temporaryPath,
            $LiteralPath,
            [bool]$Overwrite)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}
