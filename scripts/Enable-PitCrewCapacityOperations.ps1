#Requires -Version 7.0
<#
.SYNOPSIS
    Replaces one read-only connector container with the write-enabled host service.

.DESCRIPTION
    Downloads a release-pinned, self-contained connector, migrates the existing
    connector identity without displaying it, installs a native systemd or
    Windows service, and restores the stopped container if host-service startup
    fails.

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

.PARAMETER EnableManagerRecovery
    Opts this host in to typed manager-recovery commands. Recovery stays
    disabled unless this switch is supplied, so capacity-only hosts never gain
    recovery permission by upgrading.

.PARAMETER ManagerRecoveryProfiles
    Existing built-in profile identifiers allowed to receive manager-recovery
    commands. Required with -EnableManagerRecovery and independent of -Profiles.

.PARAMETER RecoveryCommandTimeoutSeconds
    Bounded local timeout for one manager-recovery invocation.

.EXAMPLE
    ./Enable-PitCrewCapacityOperations.ps1 -Version 0.3.4 -PitCrewRoot C:\dev\pitcrew -DashboardUrl https://pitcrew.example.com -Profiles copilot-cli -CapacityMaximumCeiling 30

.EXAMPLE
    ./Enable-PitCrewCapacityOperations.ps1 -Version 0.3.4 -PitCrewRoot C:\dev\pitcrew -DashboardUrl https://pitcrew.example.com -Profiles copilot-cli -CapacityMaximumCeiling 30 -EnableManagerRecovery -ManagerRecoveryProfiles copilot-cli
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
    [int]$CapacityMaximumCeiling,

    [Parameter()]
    [switch]$EnableManagerRecovery,

    [Parameter()]
    [AllowEmptyCollection()]
    [string[]]$ManagerRecoveryProfiles = @(),

    [Parameter()]
    [ValidateRange(30, 600)]
    [int]$RecoveryCommandTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$windowsServiceName = 'PitCrewConnector'
$windowsServiceDisplayName = 'PitCrew Dashboard Connector'
$linuxServiceName = 'pitcrew-connector.service'

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

function Test-WindowsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
    } finally {
        $identity.Dispose()
    }
}

function Invoke-WindowsElevatedInstaller {
    param(
        [Parameter(Mandatory)]
        [hashtable]$InstallerParameters
    )

    $powerShell = Get-Command `
        pwsh `
        -CommandType Application `
        -ErrorAction Stop |
        Select-Object -First 1
    $elevationRoot = Join-Path (
        [IO.Path]::GetTempPath()
    ) "pitcrew-connector-elevation-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $elevationRoot -Force | Out-Null
    try {
        $requestPath = Join-Path $elevationRoot 'request.clixml'
        $resultPath = Join-Path $elevationRoot 'result.json'
        @{
            ScriptPath = $PSCommandPath
            Parameters = $InstallerParameters
            ResultPath = $resultPath
        } | Export-Clixml -LiteralPath $requestPath

        $escapedRequestPath = $requestPath.Replace("'", "''")
        $elevatedCommand = @'
$ErrorActionPreference = 'Stop'
$request = Import-Clixml -LiteralPath '__REQUEST_PATH__'
$parameters = $request.Parameters
$result = [ordered]@{
    Succeeded = $false
    Error = ''
}
try {
    & $request.ScriptPath @parameters
    $result.Succeeded = $true
} catch {
    $result.Error = $_.Exception.Message
}
[IO.File]::WriteAllText(
    $request.ResultPath,
    ($result | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
if (-not $result.Succeeded) {
    exit 1
}
'@.Replace('__REQUEST_PATH__', $escapedRequestPath)
        $encodedCommand = [Convert]::ToBase64String(
            [Text.Encoding]::Unicode.GetBytes($elevatedCommand))
        try {
            Start-Process `
                -FilePath $powerShell.Source `
                -Verb RunAs `
                -ArgumentList @(
                    '-NoLogo',
                    '-NoProfile',
                    '-NonInteractive',
                    '-EncodedCommand',
                    $encodedCommand
                ) `
                -Wait |
                Out-Null
        } catch {
            throw [InvalidOperationException]::new(
                'Windows elevation was declined or unavailable. Run the installer from an elevated interactive PowerShell session.',
                $_.Exception)
        }

        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            throw 'The elevated installer did not report a result.'
        }
        $result = Get-Content `
            -LiteralPath $resultPath `
            -Raw `
            -Encoding UTF8 |
            ConvertFrom-Json
        if (-not $result.Succeeded) {
            throw "Elevated capacity-operations installation failed: $($result.Error)"
        }
    } finally {
        Remove-Item `
            -LiteralPath $elevationRoot `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

function Remove-WindowsConnectorService {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }
    try {
        if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name $Name -Force -ErrorAction Stop
            $service.WaitForStatus(
                [ServiceProcess.ServiceControllerStatus]::Stopped,
                [TimeSpan]::FromSeconds(30))
        }
    } finally {
        $service.Dispose()
    }

    Invoke-Checked -FilePath 'sc.exe' -ArgumentList @('delete', $Name)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ($true) {
        $remainingService = Get-Service `
            -Name $Name `
            -ErrorAction SilentlyContinue
        if ($null -eq $remainingService) {
            break
        }
        $remainingService.Dispose()
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "Windows service '$Name' remained registered after deletion."
        }
        Start-Sleep -Milliseconds 250
    }
}

function Get-WindowsConnectorFailureDiagnostics {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$DisplayName,

        [Parameter(Mandatory)]
        [string]$DataRoot
    )

    $diagnostics = [System.Collections.Generic.List[string]]::new()
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        $diagnostics.Add('serviceStatus=unavailable')
    } else {
        try {
            $diagnostics.Add("serviceStatus=$($service.Status)")
        } finally {
            $service.Dispose()
        }
    }

    try {
        $serviceMetadata = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='$Name'" `
            -ErrorAction Stop
        if ($null -eq $serviceMetadata) {
            $diagnostics.Add('serviceMetadata=unavailable')
        } else {
            $diagnostics.Add("serviceState=$($serviceMetadata.State)")
            $diagnostics.Add("win32ExitCode=$($serviceMetadata.ExitCode)")
            $diagnostics.Add(
                "serviceSpecificExitCode=$($serviceMetadata.ServiceSpecificExitCode)")
            $diagnostics.Add("processId=$($serviceMetadata.ProcessId)")
        }
    } catch {
        $diagnostics.Add('serviceMetadata=unavailable')
    }

    try {
        $logs = @(
            Get-ChildItem `
                -LiteralPath $DataRoot `
                -Filter 'connector-*.log' `
                -File `
                -ErrorAction Stop
        )
        $totalBytes = (
            $logs |
                Measure-Object -Property Length -Sum
        ).Sum
        if ($null -eq $totalBytes) {
            $totalBytes = 0
        }
        $diagnostics.Add("connectorLogCount=$($logs.Count)")
        $diagnostics.Add("connectorLogBytes=$totalBytes")
    } catch {
        $diagnostics.Add('connectorLogMetadata=unavailable')
    }

    try {
        $eventIds = @(
            Get-WinEvent `
                -FilterHashtable @{
                    LogName = 'System'
                    ProviderName = 'Service Control Manager'
                    StartTime = [DateTime]::UtcNow.AddMinutes(-5)
                } `
                -MaxEvents 32 `
                -ErrorAction Stop |
                Where-Object {
                    $_.Properties.Count -gt 0 -and
                    [string]$_.Properties[0].Value -in @($Name, $DisplayName)
                } |
                Select-Object -ExpandProperty Id -Unique
        )
        $eventSummary = if ($eventIds.Count -eq 0) {
            'none'
        } else {
            $eventIds -join ','
        }
        $diagnostics.Add("serviceControlEventIds=$eventSummary")
    } catch {
        $diagnostics.Add('serviceControlEventIds=unavailable')
    }

    return $diagnostics -join '; '
}

function Write-WindowsConnectorSettings {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ResolvedDashboardUrl,

        [Parameter(Mandatory)]
        [string]$DisplayName,

        [Parameter(Mandatory)]
        [string]$StateRoot,

        [Parameter(Mandatory)]
        [string]$IdentityPath,

        [Parameter(Mandatory)]
        [string]$ResolvedPitCrewRoot,

        [Parameter(Mandatory)]
        [string[]]$AllowedProfiles,

        [Parameter(Mandatory)]
        [int]$MaximumCeiling,

        [Parameter(Mandatory)]
        [string]$PowerShellExecutable,

        [Parameter(Mandatory)]
        [bool]$ManagerRecoveryEnabled,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$AllowedRecoveryProfiles,

        [Parameter(Mandatory)]
        [int]$RecoveryTimeoutSeconds,

        [Parameter(Mandatory)]
        [string]$RecoveryLedgerPath,

        [Parameter(Mandatory)]
        [string]$LogPath
    )

    $settings = [ordered]@{
        Serilog = [ordered]@{
            Using = @(
                'Serilog.Sinks.Console'
                'Serilog.Sinks.File'
            )
            MinimumLevel = [ordered]@{
                Default = 'Information'
                Override = [ordered]@{
                    Microsoft = 'Warning'
                    System = 'Warning'
                }
            }
            WriteTo = @(
                [ordered]@{
                    Name = 'Console'
                }
                [ordered]@{
                    Name = 'File'
                    Args = [ordered]@{
                        path = $LogPath
                        rollingInterval = 'Day'
                        retainedFileCountLimit = 7
                        shared = $true
                    }
                }
            )
            Enrich = @('FromLogContext')
        }
        PitCrew = [ordered]@{
            Connector = [ordered]@{
                DashboardUrl = $ResolvedDashboardUrl
                AllowInsecureHttp = $false
                EnrollmentCode = ''
                DisplayName = $DisplayName
                StateRoot = $StateRoot
                IdentityPath = $IdentityPath
                PollSeconds = 15
                HeartbeatSeconds = 30
                MaximumObservedStateBytes = 1048576
                MaximumBackoffSeconds = 300
                OperatorModeEnabled = $true
                PitCrewRoot = $ResolvedPitCrewRoot
                AllowedCapacityProfiles = $AllowedProfiles
                CapacityMaximumCeiling = $MaximumCeiling
                CapacityCommandTimeoutSeconds = 300
                PowerShellExecutable = $PowerShellExecutable
                ManagerRecoveryEnabled = $ManagerRecoveryEnabled
                AllowedManagerRecoveryProfiles = @($AllowedRecoveryProfiles)
                RecoveryCommandTimeoutSeconds = $RecoveryTimeoutSeconds
                RecoveryLedgerPath = $RecoveryLedgerPath
            }
        }
    }
    [IO.File]::WriteAllText(
        $Path,
        ($settings | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
}

if (-not ($IsLinux -or $IsWindows)) {
    throw 'Automated capacity-operator installation supports Linux and Windows hosts only.'
}
if ($IsLinux) {
    if ([int](& id -u) -ne 0) {
        throw 'Run this installer as root so it can install and start the systemd service.'
    }
} elseif (-not (Test-WindowsAdministrator)) {
    Invoke-WindowsElevatedInstaller -InstallerParameters @{
        Version = $Version
        PitCrewRoot = $PitCrewRoot
        DashboardUrl = $DashboardUrl
        Profiles = [string[]]$Profiles
        CapacityMaximumCeiling = $CapacityMaximumCeiling
        EnableManagerRecovery = [bool]$EnableManagerRecovery
        ManagerRecoveryProfiles = [string[]]$ManagerRecoveryProfiles
        RecoveryCommandTimeoutSeconds = $RecoveryCommandTimeoutSeconds
    }
    return
}

$requiredCommands = [System.Collections.Generic.List[string]]::new()
$requiredCommands.Add('docker')
$requiredCommands.Add('pwsh')
$requiredCommands.Add('tar')
if ($IsLinux) {
    $requiredCommands.Add('chown')
    $requiredCommands.Add('chmod')
    $requiredCommands.Add('systemctl')
} else {
    $requiredCommands.Add('icacls.exe')
    $requiredCommands.Add('sc.exe')
    foreach ($cmdlet in @('Get-Service', 'New-Service', 'Start-Service', 'Stop-Service')) {
        if ($null -eq (Get-Command $cmdlet -ErrorAction SilentlyContinue)) {
            throw "Required Windows service command '$cmdlet' is unavailable."
        }
    }
}
foreach ($command in $requiredCommands) {
    if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' is unavailable."
    }
}
$powerShellExecutable = [string](
    Get-Command pwsh -CommandType Application |
        Select-Object -First 1 -ExpandProperty Source
)

$resolvedPitCrewRoot = (Resolve-Path -LiteralPath $PitCrewRoot).Path
if ($IsWindows -and $resolvedPitCrewRoot.StartsWith(
        '\\',
        [StringComparison]::Ordinal)) {
    throw 'The Windows service requires PitCrewRoot to be a local path, not a network share.'
}
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
$normalizedRecoveryProfiles = @(
    $ManagerRecoveryProfiles |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Sort-Object -Unique
)
if (-not $EnableManagerRecovery) {
    if ($normalizedRecoveryProfiles.Count -gt 0) {
        throw 'ManagerRecoveryProfiles requires -EnableManagerRecovery.'
    }
} else {
    $invalidRecoveryProfiles = @(
        $normalizedRecoveryProfiles |
            Where-Object { $_ -notmatch '^[a-z][a-z0-9-]{0,31}$' }
    )
    if ($normalizedRecoveryProfiles.Count -eq 0 -or
        $invalidRecoveryProfiles.Count -gt 0) {
        throw 'ManagerRecoveryProfiles must contain one or more valid PitCrew profile identifiers when -EnableManagerRecovery is supplied.'
    }
}
foreach ($profile in @($normalizedProfiles + $normalizedRecoveryProfiles | Sort-Object -Unique)) {
    if (-not (Test-Path -LiteralPath (Join-Path $stateRoot $profile) -PathType Container)) {
        throw "Profile '$profile' does not exist below '$stateRoot'."
    }
}

$global:LASTEXITCODE = 0
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
$ridArchitecture = switch ($architecture) {
    'x64' { 'x64' }
    'arm64' { 'arm64' }
    default { throw "Unsupported host architecture '$architecture'." }
}
$rid = if ($IsWindows) {
    "win-$ridArchitecture"
} else {
    "linux-$ridArchitecture"
}
$executableName = if ($IsWindows) {
    'PitCrew.Connector.App.exe'
} else {
    'PitCrew.Connector.App'
}

$assetName = "pitcrew-connector-$Version-$rid.tar.gz"
$releaseBase = "https://github.com/ncosentino/pitcrew-dashboard/releases/download/v$Version"
if ($IsWindows) {
    $programFiles = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)
    $commonApplicationData = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonApplicationData)
    if ([string]::IsNullOrWhiteSpace($programFiles) -or
        [string]::IsNullOrWhiteSpace($commonApplicationData)) {
        throw 'Windows service installation roots could not be resolved.'
    }
    $installRoot = Join-Path $programFiles 'PitCrew\Connector'
    $dataRoot = Join-Path $commonApplicationData 'PitCrew\Connector'
    $environmentPath = $null
    $servicePath = $null
} else {
    $installRoot = '/opt/pitcrew-connector'
    $dataRoot = '/var/lib/pitcrew-connector'
    $environmentPath = '/etc/pitcrew-connector.env'
    $servicePath = '/etc/systemd/system/pitcrew-connector.service'
}

$recoveryLedgerPath = Join-Path $dataRoot 'recovery-ledger'

$existingArtifacts = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($installRoot, $dataRoot, $environmentPath, $servicePath)) {
    if (-not [string]::IsNullOrWhiteSpace($path) -and
        (Test-Path -LiteralPath $path)) {
        $existingArtifacts.Add($path)
    }
}
if ($IsWindows) {
    $existingService = Get-Service `
        -Name $windowsServiceName `
        -ErrorAction SilentlyContinue
    if ($null -ne $existingService) {
        $existingArtifacts.Add("Windows service $windowsServiceName")
        $existingService.Dispose()
    }
}
if ($existingArtifacts.Count -gt 0) {
    throw "A host connector installation already exists: $($existingArtifacts -join ', ')."
}

$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "pitcrew-connector-$([guid]::NewGuid().ToString('N'))"
$previousContainerStopped = $false
$hostArtifactsCreated = $false
$windowsServiceCreated = $false
$linuxServiceConfigured = $false

New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
try {
    $assetPath = Join-Path $temporaryRoot $assetName
    $checksumPath = "$assetPath.sha256"
    Invoke-WebRequest -Uri "$releaseBase/$assetName" -OutFile $assetPath
    Invoke-WebRequest -Uri "$releaseBase/$assetName.sha256" -OutFile $checksumPath
    $expectedHash = (
        Get-Content -LiteralPath $checksumPath -Raw -Encoding UTF8
    ).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
    if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Connector checksum '$assetName.sha256' is malformed."
    }
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
    $stagedExecutable = Join-Path $stagedInstall $executableName
    if (-not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf)) {
        throw "Connector archive '$assetName' did not contain '$executableName'."
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

    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    $hostArtifactsCreated = $true
    if ($IsWindows) {
        foreach ($permission in @(
            @($installRoot, '/inheritance:r'),
            @($installRoot, '/grant:r', '*S-1-5-18:(OI)(CI)RX'),
            @($installRoot, '/grant:r', '*S-1-5-32-544:(OI)(CI)F'),
            @($dataRoot, '/inheritance:r'),
            @($dataRoot, '/grant:r', '*S-1-5-18:(OI)(CI)F'),
            @($dataRoot, '/grant:r', '*S-1-5-32-544:(OI)(CI)F'),
            @($resolvedPitCrewRoot, '/grant', '*S-1-5-18:(OI)(CI)RX', '/T', '/C'),
            @($stateRoot, '/grant', '*S-1-5-18:(OI)(CI)M', '/T', '/C')
        )) {
            Invoke-Checked -FilePath 'icacls.exe' -ArgumentList $permission |
                Out-Null
        }
    }
    Copy-Item -Path (Join-Path $stagedInstall '*') -Destination $installRoot -Recurse -Force
    $identityPath = Join-Path $dataRoot 'identity.json'
    Copy-Item -LiteralPath $identityStagingPath -Destination $identityPath -Force

    if ($IsWindows) {
        Write-WindowsConnectorSettings `
            -Path (Join-Path $installRoot 'appsettings.json') `
            -ResolvedDashboardUrl $DashboardUrl `
            -DisplayName $displayName `
            -StateRoot $stateRoot `
            -IdentityPath $identityPath `
            -ResolvedPitCrewRoot $resolvedPitCrewRoot `
            -AllowedProfiles $normalizedProfiles `
            -MaximumCeiling $CapacityMaximumCeiling `
            -PowerShellExecutable $powerShellExecutable `
            -ManagerRecoveryEnabled ([bool]$EnableManagerRecovery) `
            -AllowedRecoveryProfiles $normalizedRecoveryProfiles `
            -RecoveryTimeoutSeconds $RecoveryCommandTimeoutSeconds `
            -RecoveryLedgerPath $recoveryLedgerPath `
            -LogPath (Join-Path $dataRoot 'connector-.log')

        $installedExecutable = Join-Path $installRoot $executableName
        $binaryPathName = '"{0}" --contentRoot "{1}"' -f (
            $installedExecutable,
            $installRoot)
        New-Service `
            -Name $windowsServiceName `
            -BinaryPathName $binaryPathName `
            -DisplayName $windowsServiceDisplayName `
            -Description 'Synchronizes PitCrew state and executes locally authorized capacity operations.' `
            -StartupType Automatic |
            Out-Null
        $windowsServiceCreated = $true
        Invoke-Checked -FilePath 'sc.exe' -ArgumentList @(
            'config',
            $windowsServiceName,
            'start=',
            'delayed-auto'
        )
        Invoke-Checked -FilePath 'sc.exe' -ArgumentList @(
            'failure',
            $windowsServiceName,
            'reset=',
            '86400',
            'actions=',
            'restart/5000/restart/15000/restart/60000'
        )
        Invoke-Checked -FilePath 'sc.exe' -ArgumentList @(
            'failureflag',
            $windowsServiceName,
            '1'
        )
        Start-Service -Name $windowsServiceName
        Start-Sleep -Seconds 5
        $service = Get-Service -Name $windowsServiceName
        try {
            if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
                $diagnostics = Get-WindowsConnectorFailureDiagnostics `
                    -Name $windowsServiceName `
                    -DisplayName $windowsServiceDisplayName `
                    -DataRoot $dataRoot
                throw "Windows service '$windowsServiceName' did not remain running. Bounded diagnostics: $diagnostics"
            }
        } finally {
            $service.Dispose()
        }
    } else {
        $serviceUser = if (-not [string]::IsNullOrWhiteSpace($env:SUDO_USER)) {
            $env:SUDO_USER
        } else {
            'root'
        }
        if ($serviceUser -notmatch '^[a-z_][a-z0-9_-]*[$]?$') {
            throw "Service user '$serviceUser' is not a valid local account name."
        }
        Invoke-Checked -FilePath 'chown' -ArgumentList @(
            '-R',
            $serviceUser,
            $dataRoot
        )
        Invoke-Checked -FilePath 'chmod' -ArgumentList @(
            '0600',
            $identityPath
        )
        Invoke-Checked -FilePath 'chmod' -ArgumentList @(
            '0755',
            (Join-Path $installRoot $executableName)
        )

        $environmentLines = [System.Collections.Generic.List[string]]::new()
        $environmentLines.Add(
            "PitCrew__Connector__DashboardUrl=$(ConvertTo-EnvironmentValue -Value ([string]$DashboardUrl))")
        $environmentLines.Add('PitCrew__Connector__AllowInsecureHttp="false"')
        $environmentLines.Add('PitCrew__Connector__EnrollmentCode=""')
        $environmentLines.Add(
            "PitCrew__Connector__DisplayName=$(ConvertTo-EnvironmentValue -Value ([string]$displayName))")
        $environmentLines.Add(
            "PitCrew__Connector__StateRoot=$(ConvertTo-EnvironmentValue -Value ([string]$stateRoot))")
        $environmentLines.Add(
            "PitCrew__Connector__IdentityPath=$(ConvertTo-EnvironmentValue -Value ([string]$identityPath))")
        $environmentLines.Add('PitCrew__Connector__OperatorModeEnabled="true"')
        $environmentLines.Add(
            "PitCrew__Connector__PitCrewRoot=$(ConvertTo-EnvironmentValue -Value ([string]$resolvedPitCrewRoot))")
        $environmentLines.Add(
            "PitCrew__Connector__CapacityMaximumCeiling=$(ConvertTo-EnvironmentValue -Value ([string]$CapacityMaximumCeiling))")
        $environmentLines.Add('PitCrew__Connector__CapacityCommandTimeoutSeconds="300"')
        $environmentLines.Add(
            "PitCrew__Connector__PowerShellExecutable=$(ConvertTo-EnvironmentValue -Value ([string]$powerShellExecutable))")
        $environmentLines.Add(
            "PitCrew__Connector__ManagerRecoveryEnabled=$(ConvertTo-EnvironmentValue -Value $(if ($EnableManagerRecovery) { 'true' } else { 'false' }))")
        $environmentLines.Add(
            "PitCrew__Connector__RecoveryCommandTimeoutSeconds=$(ConvertTo-EnvironmentValue -Value ([string]$RecoveryCommandTimeoutSeconds))")
        $environmentLines.Add(
            "PitCrew__Connector__RecoveryLedgerPath=$(ConvertTo-EnvironmentValue -Value ([string]$recoveryLedgerPath))")
        for ($index = 0; $index -lt $normalizedProfiles.Count; $index++) {
            $environmentLines.Add(
                "PitCrew__Connector__AllowedCapacityProfiles__${index}=$(ConvertTo-EnvironmentValue -Value ([string]$normalizedProfiles[$index]))")
        }
        for ($index = 0; $index -lt $normalizedRecoveryProfiles.Count; $index++) {
            $environmentLines.Add(
                "PitCrew__Connector__AllowedManagerRecoveryProfiles__${index}=$(ConvertTo-EnvironmentValue -Value ([string]$normalizedRecoveryProfiles[$index]))")
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
ExecStart=$installRoot/$executableName
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
        $linuxServiceConfigured = $true
        Invoke-Checked -FilePath 'systemctl' -ArgumentList @('daemon-reload')
        Invoke-Checked -FilePath 'systemctl' -ArgumentList @(
            'enable',
            '--now',
            $linuxServiceName
        )
        Start-Sleep -Seconds 5
        Invoke-Checked -FilePath 'systemctl' -ArgumentList @(
            'is-active',
            '--quiet',
            $linuxServiceName
        )
    }

    $previousContainerStopped = $false
    $serviceDescription = if ($IsWindows) {
        "Windows service '$windowsServiceName'"
    } else {
        "systemd service '$linuxServiceName'"
    }
    $recoveryDescription = if ($EnableManagerRecovery) {
        "manager recovery enabled for profiles: $($normalizedRecoveryProfiles -join ', ')"
    } else {
        'manager recovery disabled'
    }
    Write-Host "PitCrew capacity operations enabled through $serviceDescription for profiles: $($normalizedProfiles -join ', ') ($recoveryDescription)."
} catch {
    $installationFailure = $_
    $rollbackFailures = [System.Collections.Generic.List[string]]::new()
    $serviceRemovalSucceeded = $true

    if ($windowsServiceCreated) {
        try {
            Remove-WindowsConnectorService -Name $windowsServiceName
        } catch {
            $serviceRemovalSucceeded = $false
            $rollbackFailures.Add(
                "Windows service cleanup failed: $($_.Exception.Message)")
        }
    }
    if ($linuxServiceConfigured) {
        try {
            Invoke-Checked -FilePath 'systemctl' -ArgumentList @(
                'disable',
                '--now',
                $linuxServiceName
            )
            Remove-Item -LiteralPath $servicePath -Force -ErrorAction Stop
            Invoke-Checked -FilePath 'systemctl' -ArgumentList @('daemon-reload')
        } catch {
            $serviceRemovalSucceeded = $false
            $rollbackFailures.Add(
                "systemd service cleanup failed: $($_.Exception.Message)")
        }
    }
    if ($hostArtifactsCreated) {
        foreach ($path in @($installRoot, $dataRoot, $environmentPath)) {
            if ([string]::IsNullOrWhiteSpace($path) -or
                -not (Test-Path -LiteralPath $path)) {
                continue
            }
            try {
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            } catch {
                $rollbackFailures.Add(
                    "Host artifact cleanup failed for '$path': $($_.Exception.Message)")
            }
        }
    }
    if ($previousContainerStopped) {
        if ($serviceRemovalSucceeded) {
            try {
                Invoke-Checked -FilePath 'docker' -ArgumentList @(
                    'start',
                    $connectorId
                )
            } catch {
                $rollbackFailures.Add(
                    "Connector container restart failed: $($_.Exception.Message)")
            }
        } else {
            $rollbackFailures.Add(
                'The connector container was left stopped because the host service could not be removed safely.')
        }
    }

    if ($rollbackFailures.Count -gt 0) {
        $message = @(
            "Capacity-operations installation failed: $($installationFailure.Exception.Message)"
            "Rollback failures: $($rollbackFailures -join '; ')"
        ) -join ' '
        throw [InvalidOperationException]::new(
            $message,
            $installationFailure.Exception)
    }
    $PSCmdlet.ThrowTerminatingError($installationFailure)
} finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
