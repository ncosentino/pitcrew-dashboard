#Requires -Version 7.0
<#
.SYNOPSIS
    Replaces one read-only connector container with the write-enabled host service.

.DESCRIPTION
    Downloads a release-pinned, self-contained connector, migrates the existing
    connector identity without displaying it, installs a systemd service, and
    restores the stopped container if host-service startup fails.

.PARAMETER Version
    Dashboard release version without a leading v.

.PARAMETER PitCrewRoot
    Existing PitCrew checkout containing Setup-Runner.ps1 and .pitcrew-state.

.PARAMETER DashboardUrl
    HTTPS dashboard base URL used by the existing connector.

.PARAMETER Profiles
    Existing built-in profile identifiers allowed to receive capacity commands.

.PARAMETER CapacityMaximumCeiling
    Local maximum accepted from the dashboard.

.EXAMPLE
    ./Enable-PitCrewCapacityOperations.ps1 -Version 0.3.3 -PitCrewRoot /opt/pitcrew -DashboardUrl https://pitcrew.example.com -Profiles copilot-cli -CapacityMaximumCeiling 30
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$PitCrewRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$DashboardUrl,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Profiles,

    [Parameter(Mandatory)]
    [ValidateRange(1, 1000000)]
    [int]$CapacityMaximumCeiling
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' exited with code $LASTEXITCODE."
    }
}

function ConvertTo-EnvironmentValue {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($Value -match '[\r\n]') {
        throw 'Environment values cannot contain newlines.'
    }
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

if (-not $IsLinux) {
    throw 'Automated capacity-operator installation currently requires Linux with systemd.'
}
if ([int](& id -u) -ne 0) {
    throw 'Run this installer as root so it can install and start the systemd service.'
}

$resolvedPitCrewRoot = (Resolve-Path -LiteralPath $PitCrewRoot).Path
$setupPath = Join-Path $resolvedPitCrewRoot 'Setup-Runner.ps1'
$stateRoot = Join-Path $resolvedPitCrewRoot '.pitcrew-state'
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
    throw "PitCrew root '$resolvedPitCrewRoot' does not contain Setup-Runner.ps1 and .pitcrew-state."
}

$normalizedProfiles = @(
    $Profiles |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Sort-Object -Unique
)
$invalidProfiles = @(
    $normalizedProfiles |
        Where-Object { $_ -notmatch '^[a-z][a-z0-9-]{0,31}$' }
)
if ($normalizedProfiles.Count -eq 0 -or $invalidProfiles.Count -gt 0) {
    throw 'Profiles must contain one or more valid PitCrew profile identifiers.'
}
foreach ($profile in $normalizedProfiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $stateRoot $profile) -PathType Container)) {
        throw "Profile '$profile' does not exist below '$stateRoot'."
    }
}

$connectorIds = @(
    docker ps -q `
        --filter 'label=com.docker.compose.service=connector' 2>$null |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($LASTEXITCODE -ne 0) {
    throw 'Docker could not enumerate the existing connector container.'
}
if ($connectorIds.Count -ne 1) {
    throw "Expected exactly one running connector container, found $($connectorIds.Count)."
}
$connectorId = [string]$connectorIds[0]

$architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = switch ($architecture) {
    'x64' { 'linux-x64' }
    'arm64' { 'linux-arm64' }
    default { throw "Unsupported host architecture '$architecture'." }
}

$assetName = "pitcrew-connector-$Version-$rid.tar.gz"
$releaseBase = "https://github.com/ncosentino/pitcrew-dashboard/releases/download/v$Version"
$installRoot = '/opt/pitcrew-connector'
$dataRoot = '/var/lib/pitcrew-connector'
$environmentPath = '/etc/pitcrew-connector.env'
$servicePath = '/etc/systemd/system/pitcrew-connector.service'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "pitcrew-connector-$([guid]::NewGuid().ToString('N'))"
$previousContainerStopped = $false

New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
try {
    $assetPath = Join-Path $temporaryRoot $assetName
    $checksumPath = "$assetPath.sha256"
    Invoke-WebRequest -Uri "$releaseBase/$assetName" -OutFile $assetPath
    Invoke-WebRequest -Uri "$releaseBase/$assetName.sha256" -OutFile $checksumPath
    $expectedHash = (
        Get-Content -LiteralPath $checksumPath -Raw -Encoding UTF8
    ).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $expectedHash.ToLowerInvariant()) {
        throw "Connector asset checksum did not match '$assetName.sha256'."
    }

    $stagedInstall = Join-Path $temporaryRoot 'install'
    New-Item -ItemType Directory -Path $stagedInstall -Force | Out-Null
    Invoke-Checked -FilePath 'tar' -ArgumentList @(
        '-xzf',
        $assetPath,
        '-C',
        $stagedInstall
    )
    $stagedExecutable = Join-Path $stagedInstall 'PitCrew.Connector.App'
    if (-not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf)) {
        throw "Connector archive '$assetName' did not contain the expected executable."
    }

    $serviceUser = if (-not [string]::IsNullOrWhiteSpace($env:SUDO_USER)) {
        $env:SUDO_USER
    } else {
        'root'
    }
    if ($serviceUser -notmatch '^[a-z_][a-z0-9_-]*[$]?$') {
        throw "Service user '$serviceUser' is not a valid local account name."
    }
    $displayName = [Net.Dns]::GetHostName()
    $identityStagingPath = Join-Path $temporaryRoot 'identity.json'

    Invoke-Checked -FilePath 'docker' -ArgumentList @(
        'stop',
        '--time',
        '35',
        $connectorId
    )
    $previousContainerStopped = $true
    Invoke-Checked -FilePath 'docker' -ArgumentList @(
        'cp',
        "${connectorId}:/var/lib/pitcrew-connector/identity.json",
        $identityStagingPath
    )

    if (Test-Path -LiteralPath $installRoot) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $stagedInstall '*') -Destination $installRoot -Recurse -Force
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    Copy-Item -LiteralPath $identityStagingPath -Destination (Join-Path $dataRoot 'identity.json') -Force

    Invoke-Checked -FilePath 'chown' -ArgumentList @(
        '-R',
        $serviceUser,
        $dataRoot
    )
    Invoke-Checked -FilePath 'chmod' -ArgumentList @(
        '0600',
        (Join-Path $dataRoot 'identity.json')
    )
    Invoke-Checked -FilePath 'chmod' -ArgumentList @(
        '0755',
        (Join-Path $installRoot 'PitCrew.Connector.App')
    )

    $environmentLines = [System.Collections.Generic.List[string]]::new()
    $environmentLines.Add(
        "PitCrew__Connector__DashboardUrl=$(ConvertTo-EnvironmentValue $DashboardUrl)")
    $environmentLines.Add('PitCrew__Connector__AllowInsecureHttp="false"')
    $environmentLines.Add('PitCrew__Connector__EnrollmentCode=""')
    $environmentLines.Add(
        "PitCrew__Connector__DisplayName=$(ConvertTo-EnvironmentValue $displayName)")
    $environmentLines.Add(
        "PitCrew__Connector__StateRoot=$(ConvertTo-EnvironmentValue $stateRoot)")
    $environmentLines.Add(
        "PitCrew__Connector__IdentityPath=$(ConvertTo-EnvironmentValue (Join-Path $dataRoot 'identity.json'))")
    $environmentLines.Add('PitCrew__Connector__OperatorModeEnabled="true"')
    $environmentLines.Add(
        "PitCrew__Connector__PitCrewRoot=$(ConvertTo-EnvironmentValue $resolvedPitCrewRoot)")
    $environmentLines.Add(
        "PitCrew__Connector__CapacityMaximumCeiling=$(ConvertTo-EnvironmentValue ([string]$CapacityMaximumCeiling))")
    $environmentLines.Add('PitCrew__Connector__CapacityCommandTimeoutSeconds="300"')
    $environmentLines.Add('PitCrew__Connector__PowerShellExecutable="pwsh"')
    for ($index = 0; $index -lt $normalizedProfiles.Count; $index++) {
        $environmentLines.Add(
            "PitCrew__Connector__AllowedCapacityProfiles__${index}=$(ConvertTo-EnvironmentValue $normalizedProfiles[$index])")
    }
    [IO.File]::WriteAllLines(
        $environmentPath,
        $environmentLines,
        [Text.UTF8Encoding]::new($false))
    Invoke-Checked -FilePath 'chmod' -ArgumentList @('0600', $environmentPath)

    $service = @"
[Unit]
Description=PitCrew dashboard connector
After=network-online.target docker.service
Wants=network-online.target

[Service]
Type=simple
User=$serviceUser
WorkingDirectory=$installRoot
EnvironmentFile=$environmentPath
ExecStart=$installRoot/PitCrew.Connector.App
Restart=always
RestartSec=5
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
"@
    [IO.File]::WriteAllText(
        $servicePath,
        $service,
        [Text.UTF8Encoding]::new($false))

    Invoke-Checked -FilePath 'systemctl' -ArgumentList @('daemon-reload')
    Invoke-Checked -FilePath 'systemctl' -ArgumentList @(
        'enable',
        '--now',
        'pitcrew-connector.service'
    )
    Start-Sleep -Seconds 5
    Invoke-Checked -FilePath 'systemctl' -ArgumentList @(
        'is-active',
        '--quiet',
        'pitcrew-connector.service'
    )
    $previousContainerStopped = $false
    Write-Host "PitCrew capacity operations enabled for profiles: $($normalizedProfiles -join ', ')."
}
catch {
    if ($previousContainerStopped) {
        & systemctl disable --now pitcrew-connector.service 2>$null | Out-Null
        & docker start $connectorId 2>$null | Out-Null
    }
    throw
}
finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
