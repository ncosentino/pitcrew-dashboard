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
    if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) {
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
        [string]$plan.topologyProfile -cne 'portable' -or
        [string]$plan.dashboard.commit -notmatch '^[a-f0-9]{40}$' -or
        [string]$plan.pitCrew.commit -notmatch '^[a-f0-9]{40}$') {
        throw 'The canary plan manifest is invalid.'
    }
    return $plan
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
        [object]$Value
    )

    $temporaryPath = "$LiteralPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            (($Value | ConvertTo-Json -Depth 20) + "`n"),
            [Text.UTF8Encoding]::new($false)
        )
        [IO.File]::Move($temporaryPath, $LiteralPath, $false)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}
