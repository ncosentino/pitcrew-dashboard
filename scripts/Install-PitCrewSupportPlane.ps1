#Requires -Version 7.0
<#
.SYNOPSIS
Installs and manages the isolated PitCrew support agent and diagnostics broker.

.DESCRIPTION
Uses separate service identities, state roots, and platform IPC controls. Release
updates are staged before service switching and retain one rollback target.
This installer preserves the complete protected support identity state during
uninstall and never reads or rewrites private key values.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(
        'Install',
        'Update',
        'Enable',
        'Disable',
        'Uninstall',
        'Rollback',
        'RepairEvidenceAcl',
        'Verify'
    )]
    [string]$Action,

    [ValidatePattern(
        '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$'
    )]
    [string]$Version = '',

    [string]$PitCrewRoot = '',

    [string[]]$Profiles = @(),

    [string]$AgentSettingsPath = '',

    [string]$AgentArchivePath = '',

    [string]$AgentChecksumPath = '',

    [string]$BrokerArchivePath = '',

    [string]$BrokerChecksumPath = '',

    [ValidateSet('PreserveKeys')]
    [string]$IdentityHandling = 'PreserveKeys',

    [switch]$AllowMachineChanges
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$windowsAgentService = 'PitCrewSupportAgent'
$windowsBrokerService = 'PitCrewSupportBroker'
$windowsFirewallRule = 'PitCrewSupportBroker-Outbound-Block'
$linuxAgentService = 'pitcrew-support-agent.service'
$linuxBrokerService = 'pitcrew-support-broker.service'
$linuxAgentUser = 'pitcrew-support-agent'
$linuxBrokerUser = 'pitcrew-support-broker'
$linuxIpcGroup = 'pitcrew-support-ipc'
$pipeName = 'pitcrew-support-broker-v1'
$socketPath = '/run/pitcrew-support/broker.sock'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ArgumentList
    )

    $global:LASTEXITCODE = 0
    & $FilePath @ArgumentList | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $operation = if ($ArgumentList.Count -gt 0) {
            " $($ArgumentList[0])"
        } else {
            ''
        }
        throw "'$FilePath$operation' exited with code $LASTEXITCODE."
    }
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

function Assert-PlatformAdministrator {
    if ($IsWindows) {
        if (-not (Test-WindowsAdministrator)) {
            throw 'Run the support-plane installer from an elevated PowerShell session.'
        }
        return
    }
    if ($IsLinux) {
        $uid = & id -u
        if ($LASTEXITCODE -ne 0 -or [int]$uid -ne 0) {
            throw 'Run the support-plane installer as root.'
        }
        return
    }
    throw 'The support-plane installer supports Windows and Linux only.'
}

function Get-PlatformPaths {
    if ($IsWindows) {
        $programFiles = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFiles)
        $programData = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::CommonApplicationData)
        return @{
            AgentInstallRoot = Join-Path $programFiles 'PitCrew\Support\Agent'
            BrokerInstallRoot = Join-Path $programFiles 'PitCrew\Support\Broker'
            AgentStateRoot = Join-Path $programData 'PitCrew\Support\Agent'
            BrokerStateRoot = Join-Path $programData 'PitCrew\Support\Broker'
            InstallerStateRoot = Join-Path $programData 'PitCrew\Support\Installer'
            ConnectorHealthRoot = Join-Path $programData 'PitCrew\Connector\health'
            LockPath = Join-Path $programData 'PitCrew\Support\Lock\lifecycle.lock'
            AgentUnitPath = $null
            BrokerUnitPath = $null
        }
    }
    return @{
        AgentInstallRoot = '/opt/pitcrew-support-agent'
        BrokerInstallRoot = '/opt/pitcrew-support-broker'
        AgentStateRoot = '/var/lib/pitcrew-support-agent'
        BrokerStateRoot = '/var/lib/pitcrew-support-broker'
        InstallerStateRoot = '/var/lib/pitcrew-support-installer'
        ConnectorHealthRoot = '/var/lib/pitcrew-connector/health'
        LockPath = '/run/lock/pitcrew-support-plane/lifecycle.lock'
        AgentUnitPath = '/etc/systemd/system/pitcrew-support-agent.service'
        BrokerUnitPath = '/etc/systemd/system/pitcrew-support-broker.service'
    }
}

function Get-RuntimeIdentifier {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::
        OSArchitecture.ToString().ToLowerInvariant()
    if ($architecture -notin @('x64', 'arm64')) {
        throw 'Support-plane packages are available for x64 and arm64 only.'
    }
    if ($IsWindows) {
        return "win-$architecture"
    }
    return "linux-$architecture"
}

function Get-EvidencePolicyPath {
    $packaged = Join-Path $PSScriptRoot 'support-evidence-policy-v0.10.0.json'
    if (Test-Path -LiteralPath $packaged -PathType Leaf) {
        return $packaged
    }
    $repositoryPolicy = Join-Path (
        Resolve-Path (Join-Path $PSScriptRoot '..')
    ).Path 'assets' 'support-plane' 'support-evidence-policy-v0.10.0.json'
    if (-not (Test-Path -LiteralPath $repositoryPolicy -PathType Leaf)) {
        throw 'The product-owned PitCrew v0.10.0 evidence policy is missing.'
    }
    return $repositoryPolicy
}

function Get-EvidencePolicy {
    $policy = Get-Content `
        -LiteralPath (Get-EvidencePolicyPath) `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
    if ($policy.schemaVersion -ne 1 -or
        $policy.pitCrewVersion -ne '0.10.0' -or
        $policy.pitCrewCommit -ne '4d30a031' -or
        $policy.collectorRelativePath -ne
            'plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts/Collect-PitCrewDiagnostics.ps1' -or
        $policy.collectorSha256 -ne
            '01e8fbcb54ec7f79d8403284d521c0d98956be2f4a617aa881d490b28f88e0a3' -or
        $policy.profileStateRootAccess -ne
            'enumerate-profile-directories-only' -or
        (@($policy.installationSentinels) -join ',') -ne
            'Setup-Runner.ps1,RunnerProfiles.Functions.ps1,docker-compose.yml' -or
        (@($policy.profileProjectionFiles) -join ',') -ne
            'desired-capacity.json,acknowledged-capacity.json,static-profile.json,observed-state.json' -or
        (@($policy.connectorHealthFiles) -join ',') -ne
            'connector-health.json,connector-events.jsonl') {
        throw 'The product-owned PitCrew v0.10.0 evidence policy is invalid.'
    }
    return $policy
}

function Test-LinkedPathComponent {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    $fullRoot = [IO.Path]::GetFullPath($Root)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($fullRoot, $fullPath)
    if ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith(
            "..$([IO.Path]::DirectorySeparatorChar)",
            [StringComparison]::Ordinal)) {
        throw 'A support evidence path escaped its locally selected root.'
    }
    $current = $fullRoot
    if (Test-Path -LiteralPath $current) {
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $true
        }
    }
    foreach ($segment in $relative.Split(
        [IO.Path]::DirectorySeparatorChar,
        [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band
                    [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }
    }
    return $false
}

function Get-ManifestPath {
    param([Parameter(Mandatory)][hashtable]$Paths)
    return Join-Path $Paths.InstallerStateRoot 'install-state.json'
}

function Get-InstallManifest {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $manifestPath = Get-ManifestPath -Paths $Paths
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return $null
    }
    return Get-Content `
        -LiteralPath $manifestPath `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
}

function Write-InstallManifest {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$CurrentVersion,
        [AllowEmptyString()][string]$PreviousVersion,
        [Parameter(Mandatory)][bool]$Enabled
    )

    New-Item `
        -ItemType Directory `
        -Path $Paths.InstallerStateRoot `
        -Force |
        Out-Null
    $manifest = [ordered]@{
        schemaVersion = 1
        currentVersion = $CurrentVersion
        previousVersion = $PreviousVersion
        enabled = $Enabled
        identityContract = 'support-node-identity-v1'
    }
    $manifestPath = Get-ManifestPath -Paths $Paths
    $temporaryPath = "$manifestPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            ($manifest | ConvertTo-Json -Depth 5),
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $manifestPath, $true)
    } finally {
        Remove-Item `
            -LiteralPath $temporaryPath `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

function Assert-NoAmbiguousInstallation {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [AllowNull()][object]$Manifest
    )

    if ($null -ne $Manifest) {
        return
    }
    if ($IsWindows) {
        $services = @(
            Get-Service `
                -Name $windowsAgentService, $windowsBrokerService `
                -ErrorAction SilentlyContinue
        )
        try {
            if ($services.Count -gt 0) {
                throw 'An unmanaged or partial privileged support installation already exists.'
            }
        } finally {
            $services | ForEach-Object Dispose
        }
        if ($null -ne (
                Get-NetFirewallRule `
                    -Name $windowsFirewallRule `
                    -ErrorAction SilentlyContinue
            )) {
            throw 'An unmanaged or partial privileged support installation already exists.'
        }
    } elseif (
        (Test-Path -LiteralPath $Paths.AgentUnitPath -PathType Leaf) -or
        (Test-Path -LiteralPath $Paths.BrokerUnitPath -PathType Leaf)
    ) {
        throw 'An unmanaged or partial privileged support installation already exists.'
    }
    foreach ($root in @(
        $Paths.AgentInstallRoot,
        $Paths.BrokerInstallRoot,
        $Paths.BrokerStateRoot,
        $Paths.InstallerStateRoot
    )) {
        if (Test-Path -LiteralPath $root) {
            throw 'An unmanaged or partial privileged support installation already exists.'
        }
    }
    if (Test-Path -LiteralPath $Paths.AgentStateRoot) {
        $preservedMarker = Join-Path (
            $Paths.AgentStateRoot
        ) 'identity-preserved.json'
        $preservedSettings = Join-Path $Paths.AgentStateRoot 'appsettings.json'
        if (-not (Test-Path -LiteralPath $preservedMarker -PathType Leaf) -or
            -not (Test-Path -LiteralPath $preservedSettings -PathType Leaf)) {
            throw 'An unmanaged or partial privileged support installation already exists.'
        }
    }
    if ($IsLinux) {
        foreach ($identity in @(
            $linuxAgentUser,
            $linuxBrokerUser,
            $linuxIpcGroup
        )) {
            & getent passwd $identity | Out-Null
            $userExists = $LASTEXITCODE -eq 0
            & getent group $identity | Out-Null
            $groupExists = $LASTEXITCODE -eq 0
            if ($userExists -or $groupExists) {
                throw 'An unmanaged or partial privileged support installation already exists.'
            }
        }
    }
}

function Assert-MutatingActionAllowed {
    if (-not $AllowMachineChanges) {
        throw 'Pass -AllowMachineChanges for support-plane lifecycle changes.'
    }
}

function Enter-InstallerLock {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $lockRoot = Split-Path $Paths.LockPath -Parent
    if ($IsWindows) {
        New-Item -ItemType Directory -Path $lockRoot -Force | Out-Null
        $security = [Security.AccessControl.DirectorySecurity]::new()
        $security.SetAccessRuleProtection(
            $true,
            $false)
        foreach ($sidType in @(
            [Security.Principal.WellKnownSidType]::LocalSystemSid,
            [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid
        )) {
            $security.AddAccessRule(
                [Security.AccessControl.FileSystemAccessRule]::new(
                    [Security.Principal.SecurityIdentifier]::new(
                        $sidType,
                        $null),
                    [Security.AccessControl.FileSystemRights]::FullControl,
                    (
                        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                        [Security.AccessControl.InheritanceFlags]::ObjectInherit
                    ),
                    [Security.AccessControl.PropagationFlags]::None,
                    [Security.AccessControl.AccessControlType]::Allow))
        }
        Set-Acl -LiteralPath $lockRoot -AclObject $security
    } else {
        if (Test-Path -LiteralPath $lockRoot) {
            $lockRootItem = Get-Item -LiteralPath $lockRoot -Force
            if (-not $lockRootItem.PSIsContainer -or
                ($lockRootItem.Attributes -band
                    [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'The privileged installer lock directory is invalid.'
            }
            $owner = (& stat '--format=%u:%g' -- $lockRoot).Trim()
            if ($LASTEXITCODE -ne 0 -or $owner -cne '0:0') {
                throw 'The privileged installer lock directory owner is invalid.'
            }
        } else {
            New-Item -ItemType Directory -Path $lockRoot | Out-Null
        }
        Invoke-Checked chmod @('700', $lockRoot)
    }
    if (Test-Path -LiteralPath $Paths.LockPath) {
        $lockItem = Get-Item -LiteralPath $Paths.LockPath -Force
        if ($lockItem.PSIsContainer -or
            ($lockItem.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The privileged installer lock file is invalid.'
        }
    }
    try {
        $stream = [IO.File]::Open(
            $Paths.LockPath,
            [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
        if ($IsLinux) {
            [IO.File]::SetUnixFileMode(
                $Paths.LockPath,
                [System.IO.UnixFileMode]::UserRead -bor
                    [System.IO.UnixFileMode]::UserWrite)
        }
        return $stream
    } catch [IO.IOException] {
        throw 'Another privileged support-plane lifecycle action is already running.'
    }
}

function Assert-InstallInputs {
    param(
        [Parameter(Mandatory)][bool]$RequiresSettings,
        [Parameter(Mandatory)][hashtable]$Paths
    )

    if ([string]::IsNullOrWhiteSpace($Version) -or
        [string]::IsNullOrWhiteSpace($PitCrewRoot) -or
        $Profiles.Count -eq 0) {
        throw 'Install and update require Version, PitCrewRoot, and Profiles.'
    }
    if ($Profiles | Where-Object {
            $_ -notmatch '^[a-z][a-z0-9-]{0,31}$'
        }) {
        throw 'Every support profile must satisfy the PitCrew profile-ID contract.'
    }
    if (@($Profiles | Sort-Object -Unique).Count -ne $Profiles.Count) {
        throw 'Support profiles must be unique.'
    }
    if (-not [IO.Path]::IsPathRooted($PitCrewRoot)) {
        throw 'PitCrewRoot must be an absolute local path.'
    }
    if ($IsLinux -and $PitCrewRoot -match '[\s:%]') {
        throw 'Linux PitCrewRoot must not contain whitespace or systemd path metacharacters.'
    }
    if ($RequiresSettings) {
        $preservedSettings = Join-Path $Paths.AgentStateRoot 'appsettings.json'
        if (-not (Test-Path -LiteralPath $AgentSettingsPath -PathType Leaf) -and
            -not (Test-Path -LiteralPath $preservedSettings -PathType Leaf)) {
            throw 'Initial installation requires an existing protected agent settings file.'
        }
    }
    foreach ($sentinel in (Get-EvidencePolicy).installationSentinels) {
        $sentinelPath = Join-Path $PitCrewRoot $sentinel
        if (-not (Test-Path `
                -LiteralPath $sentinelPath `
                -PathType Leaf)) {
            throw 'PitCrewRoot does not match the supported v0.10.0 installation contract.'
        }
        if (Test-LinkedPathComponent -Root $PitCrewRoot -Path $sentinelPath) {
            throw 'Linked PitCrew installation evidence is not supported.'
        }
    }
    $rootItem = Get-Item -LiteralPath $PitCrewRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Linked PitCrew installation roots are not supported.'
    }
    $policy = Get-EvidencePolicy
    $collector = Join-Path (
        $PitCrewRoot
    ) ([string]$policy.collectorRelativePath).Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $collector -PathType Leaf)) {
        throw 'The fixed PitCrew v0.10.0 diagnostics collector is missing.'
    }
    $collectorHash = (
        Get-FileHash -LiteralPath $collector -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($collectorHash -cne [string]$policy.collectorSha256) {
        throw 'The fixed PitCrew v0.10.0 diagnostics collector hash is invalid.'
    }
    if (Test-LinkedPathComponent -Root $PitCrewRoot -Path $collector) {
        throw 'Linked PitCrew installation evidence is not supported.'
    }
    foreach ($profile in $Profiles) {
        $profileRoot = Join-Path (
            Join-Path $PitCrewRoot '.pitcrew-state'
        ) $profile
        if (-not (Test-Path -LiteralPath $profileRoot -PathType Container)) {
            throw 'A locally selected support profile is not configured.'
        }
        if (Test-LinkedPathComponent -Root $PitCrewRoot -Path $profileRoot) {
            throw 'Linked PitCrew profile state is not supported.'
        }
    }
    $connectorAnchor = [IO.Path]::GetPathRoot(
        [IO.Path]::GetFullPath($Paths.ConnectorHealthRoot))
    if (Test-LinkedPathComponent `
            -Root $connectorAnchor `
            -Path $Paths.ConnectorHealthRoot) {
        throw 'Linked connector-health evidence paths are not supported.'
    }
}

function Copy-VerifiedAsset {
    param(
        [Parameter(Mandatory)][string]$Component,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$StagingRoot,
        [AllowEmptyString()][string]$ArchivePath,
        [AllowEmptyString()][string]$ChecksumPath
    )

    $assetName = "pitcrew-support-$Component-$Version-$RuntimeIdentifier.tar.gz"
    $destination = Join-Path $StagingRoot $assetName
    $checksumDestination = "$destination.sha256"
    if ([string]::IsNullOrWhiteSpace($ArchivePath) -and
        [string]::IsNullOrWhiteSpace($ChecksumPath)) {
        $releaseRoot =
            "https://github.com/ncosentino/pitcrew-dashboard/releases/download/v$Version"
        Invoke-WebRequest `
            -Uri "$releaseRoot/$assetName" `
            -OutFile $destination
        Invoke-WebRequest `
            -Uri "$releaseRoot/$assetName.sha256" `
            -OutFile $checksumDestination
    } elseif (
        (Test-Path -LiteralPath $ArchivePath -PathType Leaf) -and
        (Test-Path -LiteralPath $ChecksumPath -PathType Leaf)
    ) {
        Copy-Item -LiteralPath $ArchivePath -Destination $destination -Force
        Copy-Item `
            -LiteralPath $ChecksumPath `
            -Destination $checksumDestination `
            -Force
    } else {
        throw "Both local $Component archive and checksum paths are required."
    }
    $expected = (
        Get-Content -LiteralPath $checksumDestination -Raw -Encoding UTF8
    ).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]
    if ($expected -notmatch '^[0-9a-f]{64}$') {
        throw "The $Component checksum file is invalid."
    }
    $actual = (
        Get-FileHash -LiteralPath $destination -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($actual -cne $expected) {
        throw "The $Component archive checksum did not match."
    }
    return $destination
}

function Stage-Release {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $runtimeIdentifier = Get-RuntimeIdentifier
    $stagingRoot = Join-Path (
        $Paths.InstallerStateRoot
    ) "staging-$([Guid]::NewGuid().ToString('N'))"
    $agentVersionRoot = ''
    $brokerVersionRoot = ''
    $agentMoved = $false
    $brokerMoved = $false
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    try {
        $agentArchive = Copy-VerifiedAsset `
            -Component 'agent' `
            -RuntimeIdentifier $runtimeIdentifier `
            -StagingRoot $stagingRoot `
            -ArchivePath $AgentArchivePath `
            -ChecksumPath $AgentChecksumPath
        $brokerArchive = Copy-VerifiedAsset `
            -Component 'broker' `
            -RuntimeIdentifier $runtimeIdentifier `
            -StagingRoot $stagingRoot `
            -ArchivePath $BrokerArchivePath `
            -ChecksumPath $BrokerChecksumPath
        $agentStaged = Join-Path $stagingRoot 'agent'
        $brokerStaged = Join-Path $stagingRoot 'broker'
        New-Item -ItemType Directory -Path $agentStaged -Force | Out-Null
        New-Item -ItemType Directory -Path $brokerStaged -Force | Out-Null
        Invoke-Checked tar @('-C', $agentStaged, '-xzf', $agentArchive)
        Invoke-Checked tar @('-C', $brokerStaged, '-xzf', $brokerArchive)
        foreach ($item in @(
            Get-ChildItem -LiteralPath $agentStaged -Recurse -Force
            Get-ChildItem -LiteralPath $brokerStaged -Recurse -Force
        )) {
            if (($item.Attributes -band
                    [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Support archives must not contain linked payloads.'
            }
        }
        $agentExecutable = if ($IsWindows) {
            'PitCrew.Support.Agent.App.exe'
        } else {
            'PitCrew.Support.Agent.App'
        }
        $brokerExecutable = if ($IsWindows) {
            'PitCrew.Support.Broker.App.exe'
        } else {
            'PitCrew.Support.Broker.App'
        }
        if (-not (Test-Path `
                -LiteralPath (Join-Path $agentStaged $agentExecutable) `
                -PathType Leaf) -or
            -not (Test-Path `
                -LiteralPath (Join-Path $brokerStaged $brokerExecutable) `
                -PathType Leaf)) {
            throw 'A support archive omitted its expected executable.'
        }
        $agentVersionRoot = Join-Path (
            Join-Path $Paths.AgentInstallRoot 'versions'
        ) $Version
        $brokerVersionRoot = Join-Path (
            Join-Path $Paths.BrokerInstallRoot 'versions'
        ) $Version
        if ((Test-Path -LiteralPath $agentVersionRoot) -or
            (Test-Path -LiteralPath $brokerVersionRoot)) {
            throw 'The requested support version is already staged or installed.'
        }
        New-Item `
            -ItemType Directory `
            -Path (Split-Path $agentVersionRoot -Parent) `
            -Force |
            Out-Null
        New-Item `
            -ItemType Directory `
            -Path (Split-Path $brokerVersionRoot -Parent) `
            -Force |
            Out-Null
        Move-Item -LiteralPath $agentStaged -Destination $agentVersionRoot
        $agentMoved = $true
        Move-Item -LiteralPath $brokerStaged -Destination $brokerVersionRoot
        $brokerMoved = $true
        if ($IsLinux) {
            Invoke-Checked chmod @(
                '755',
                (Join-Path $agentVersionRoot $agentExecutable),
                (Join-Path $brokerVersionRoot $brokerExecutable)
            )
        }
        return @{
            AgentVersionRoot = $agentVersionRoot
            BrokerVersionRoot = $brokerVersionRoot
            AgentExecutable = Join-Path $agentVersionRoot $agentExecutable
            BrokerExecutable = Join-Path $brokerVersionRoot $brokerExecutable
        }
    } catch {
        if ($agentMoved) {
            Remove-Item `
                -LiteralPath $agentVersionRoot `
                -Recurse `
                -Force `
                -ErrorAction SilentlyContinue
        }
        if ($brokerMoved) {
            Remove-Item `
                -LiteralPath $brokerVersionRoot `
                -Recurse `
                -Force `
                -ErrorAction SilentlyContinue
        }
        throw
    } finally {
        Remove-Item `
            -LiteralPath $stagingRoot `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

function Remove-ObsoleteSupportVersions {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string[]]$KeepVersions
    )

    $keep = [Collections.Generic.HashSet[string]]::new(
        $KeepVersions,
        [StringComparer]::Ordinal)
    foreach ($installRoot in @(
        $Paths.AgentInstallRoot,
        $Paths.BrokerInstallRoot
    )) {
        $versionsRoot = Join-Path $installRoot 'versions'
        if (-not (Test-Path -LiteralPath $versionsRoot -PathType Container)) {
            continue
        }
        foreach ($directory in Get-ChildItem `
            -LiteralPath $versionsRoot `
            -Directory `
            -Force) {
            if (-not $keep.Contains($directory.Name)) {
                Remove-Item `
                    -LiteralPath $directory.FullName `
                    -Recurse `
                    -Force
            }
        }
    }
}

function Assert-InstalledSupportVersion {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$InstalledVersion
    )

    $agentExecutable = if ($IsWindows) {
        'PitCrew.Support.Agent.App.exe'
    } else {
        'PitCrew.Support.Agent.App'
    }
    $brokerExecutable = if ($IsWindows) {
        'PitCrew.Support.Broker.App.exe'
    } else {
        'PitCrew.Support.Broker.App'
    }
    foreach ($path in @(
        (Join-Path (
            Join-Path (
                Join-Path $Paths.AgentInstallRoot 'versions'
            ) $InstalledVersion
        ) $agentExecutable),
        (Join-Path (
            Join-Path (
                Join-Path $Paths.BrokerInstallRoot 'versions'
            ) $InstalledVersion
        ) $brokerExecutable)
    )) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Installed support version '$InstalledVersion' is incomplete."
        }
    }
}

function Assert-LinuxCurrentVersion {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$InstalledVersion
    )

    foreach ($installRoot in @(
        $Paths.AgentInstallRoot,
        $Paths.BrokerInstallRoot
    )) {
        $current = Join-Path $installRoot 'current'
        $item = Get-Item -LiteralPath $current -Force
        $expected = [IO.Path]::GetFullPath(
            (Join-Path (
                Join-Path $installRoot 'versions'
            ) $InstalledVersion))
        $targets = @($item.Target)
        $actual = if ($targets.Count -eq 1) {
            $target = [string]$targets[0]
            if ([IO.Path]::IsPathRooted($target)) {
                [IO.Path]::GetFullPath($target)
            } else {
                [IO.Path]::GetFullPath((Join-Path $installRoot $target))
            }
        } else {
            ''
        }
        if ($item.LinkType -ne 'SymbolicLink' -or
            -not $actual.Equals(
                $expected,
                [StringComparison]::Ordinal)) {
            throw 'The active Linux support version link is not exact.'
        }
    }
}

function Get-WindowsServiceSid {
    param([Parameter(Mandatory)][string]$ServiceName)

    $account = [Security.Principal.NTAccount]::new(
        "NT SERVICE\$ServiceName")
    return $account.Translate(
        [Security.Principal.SecurityIdentifier]).Value
}

function Set-WindowsProtectedDirectoryAcl {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ServiceSid,
        [Parameter(Mandatory)][string]$ServiceRights
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Invoke-Checked icacls.exe @(
        $Path,
        '/inheritance:r',
        '/grant:r',
        '*S-1-5-18:(OI)(CI)F',
        '*S-1-5-32-544:(OI)(CI)F',
        "*$ServiceSid`:(OI)(CI)$ServiceRights",
        '/T',
        '/C'
    )
}

function Grant-WindowsServiceParentTraversal {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$AgentSid,
        [Parameter(Mandatory)][string]$BrokerSid
    )

    $parents = @(
        Split-Path $Paths.AgentInstallRoot -Parent
        Split-Path (Split-Path $Paths.AgentInstallRoot -Parent) -Parent
        Split-Path $Paths.AgentStateRoot -Parent
        Split-Path (Split-Path $Paths.AgentStateRoot -Parent) -Parent
    ) | Sort-Object -Unique
    foreach ($parent in $parents) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Invoke-Checked icacls.exe @(
            $parent,
            '/grant',
            "*$AgentSid`:(X,RA)",
            "*$BrokerSid`:(X,RA)"
        )
    }
}

function Revoke-WindowsServiceParentTraversal {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$AgentSid,
        [Parameter(Mandatory)][string]$BrokerSid
    )

    $parents = @(
        Split-Path $Paths.AgentInstallRoot -Parent
        Split-Path (Split-Path $Paths.AgentInstallRoot -Parent) -Parent
        Split-Path $Paths.AgentStateRoot -Parent
        Split-Path (Split-Path $Paths.AgentStateRoot -Parent) -Parent
    ) | Sort-Object -Unique
    foreach ($parent in $parents) {
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            continue
        }
        Invoke-Checked icacls.exe @(
            $parent,
            '/remove:g',
            "*$AgentSid",
            "*$BrokerSid"
        )
    }
}

function Protect-StateRootsForInstaller {
    param([Parameter(Mandatory)][hashtable]$Paths)

    foreach ($stateRoot in @(
        $Paths.AgentStateRoot,
        $Paths.BrokerStateRoot
    )) {
        New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
        if ($IsWindows) {
            Invoke-Checked icacls.exe @(
                $stateRoot,
                '/inheritance:r',
                '/grant:r',
                '*S-1-5-18:(OI)(CI)F',
                '*S-1-5-32-544:(OI)(CI)F',
                '/T',
                '/C'
            )
        } else {
            Invoke-Checked chown @('-R', 'root:root', $stateRoot)
            Invoke-Checked chmod @('-R', 'u=rwX,go=', $stateRoot)
        }
    }
}

function Grant-WindowsBrokerEvidence {
    param(
        [Parameter(Mandatory)][string]$ResolvedPitCrewRoot,
        [Parameter(Mandatory)][string[]]$AllowedProfiles,
        [Parameter(Mandatory)][string]$BrokerSid,
        [Parameter(Mandatory)][string]$AgentSid,
        [Parameter(Mandatory)][hashtable]$Paths
    )

    $policy = Get-EvidencePolicy
    Invoke-Checked icacls.exe @(
        $ResolvedPitCrewRoot,
        '/remove:g',
        "*$BrokerSid",
        "*$AgentSid",
        '/T',
        '/C'
    )
    Invoke-Checked icacls.exe @(
        $ResolvedPitCrewRoot,
        '/remove:d',
        "*$AgentSid",
        '/T',
        '/C'
    )
    Invoke-Checked icacls.exe @(
        $ResolvedPitCrewRoot,
        '/deny',
        "*$AgentSid`:(OI)(CI)(F)"
    )
    Invoke-Checked icacls.exe @(
        $ResolvedPitCrewRoot,
        '/grant',
        "*$BrokerSid`:(X,RA)"
    )
    foreach ($sentinel in $policy.installationSentinels) {
        Invoke-Checked icacls.exe @(
            (Join-Path $ResolvedPitCrewRoot $sentinel),
            '/grant',
            "*$BrokerSid`:(RA)"
        )
    }
    $stateRoot = Join-Path $ResolvedPitCrewRoot '.pitcrew-state'
    if ($policy.profileStateRootAccess -ne
        'enumerate-profile-directories-only') {
        throw 'The PitCrew profile-state access contract is unsupported.'
    }
    Invoke-Checked icacls.exe @(
        $stateRoot,
        '/grant',
        "*$BrokerSid`:(RD,X,RA)"
    )
    foreach ($profile in $AllowedProfiles) {
        $profileRoot = Join-Path $stateRoot $profile
        Invoke-Checked icacls.exe @(
            $profileRoot,
            '/grant',
            "*$BrokerSid`:(X,RA)"
        )
        foreach ($fileName in $policy.profileProjectionFiles) {
            $path = Join-Path $profileRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Invoke-Checked icacls.exe @(
                    $path,
                    '/grant',
                    "*$BrokerSid`:(R)"
                )
            }
        }
    }
    $collector = Join-Path (
        $ResolvedPitCrewRoot
    ) ([string]$policy.collectorRelativePath).Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    $current = Split-Path $collector -Parent
    while ($current.StartsWith(
            $ResolvedPitCrewRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        Invoke-Checked icacls.exe @(
            $current,
            '/grant',
            "*$BrokerSid`:(X,RA)"
        )
        if ($current.Equals(
                $ResolvedPitCrewRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = Split-Path $current -Parent
    }
    Invoke-Checked icacls.exe @(
        $collector,
        '/grant',
        "*$BrokerSid`:(R)"
    )
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot) {
        Invoke-Checked icacls.exe @(
            $connectorRoot,
            '/remove:g',
            "*$BrokerSid",
            "*$AgentSid",
            '/T',
            '/C'
        )
        Invoke-Checked icacls.exe @(
            $connectorRoot,
            '/remove:d',
            "*$AgentSid",
            '/T',
            '/C'
        )
        Invoke-Checked icacls.exe @(
            $connectorRoot,
            '/deny',
            "*$AgentSid`:(OI)(CI)(F)"
        )
        Invoke-Checked icacls.exe @(
            $connectorRoot,
            '/grant',
            "*$BrokerSid`:(X,RA)"
        )
    }
    if (Test-Path -LiteralPath $Paths.ConnectorHealthRoot) {
        Invoke-Checked icacls.exe @(
            $Paths.ConnectorHealthRoot,
            '/grant',
            "*$BrokerSid`:(X,RA)"
        )
        foreach ($fileName in $policy.connectorHealthFiles) {
            $path = Join-Path $Paths.ConnectorHealthRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Invoke-Checked icacls.exe @(
                    $path,
                    '/grant',
                    "*$BrokerSid`:(R)"
                )
            }
        }
    }
}

function Set-WindowsServiceDefinition {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$Arguments,
        [Parameter(Mandatory)][string]$BundleExtractRoot,
        [Parameter(Mandatory)]
        [string[]]$RequiredPrivileges
    )

    $binaryPath = "`"$Executable`" $Arguments"
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Invoke-Checked sc.exe @(
            'create',
            $Name,
            'binPath=',
            $binaryPath,
            'start=',
            'auto',
            'DisplayName=',
            $DisplayName
        )
    } else {
        $service.Dispose()
        Invoke-Checked sc.exe @(
            'config',
            $Name,
            'binPath=',
            $binaryPath,
            'start=',
            'auto',
            'DisplayName=',
            $DisplayName
        )
    }
    Invoke-Checked sc.exe @(
        'config',
        $Name,
        'obj=',
        "NT SERVICE\$Name"
    )
    Invoke-Checked sc.exe @(
        'sdset',
        $Name,
        'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)'
    )
    New-Item -ItemType Directory -Path $BundleExtractRoot -Force | Out-Null
    New-ItemProperty `
        -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$Name" `
        -Name 'Environment' `
        -PropertyType MultiString `
        -Value @("DOTNET_BUNDLE_EXTRACT_BASE_DIR=$BundleExtractRoot") `
        -Force |
        Out-Null
    Invoke-Checked sc.exe @('sidtype', $Name, 'unrestricted')
    Invoke-Checked sc.exe @(
        'privs',
        $Name,
        ($RequiredPrivileges -join '/')
    )
    Invoke-Checked sc.exe @(
        'failure',
        $Name,
        'reset=',
        '86400',
        'actions=',
        'restart/5000/restart/15000/none/0'
    )
}

function Write-BrokerSettings {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$ResolvedPitCrewRoot,
        [Parameter(Mandatory)][string[]]$AllowedProfiles,
        [AllowEmptyString()][string]$AgentSid,
        [AllowEmptyString()][string]$BrokerSid,
        [AllowNull()][object]$AgentUid,
        [AllowNull()][object]$BrokerUid,
        [AllowNull()][object]$IpcGroupGid
    )

    $settings = [ordered]@{
        PitCrewSupport = [ordered]@{
            Broker = [ordered]@{
                PitCrewRoot = $ResolvedPitCrewRoot
                AllowedProfiles = $AllowedProfiles -join ','
                PipeName = $pipeName
                SocketPath = $socketPath
                ExpectedAgentSid = $AgentSid
                BrokerServiceSid = $BrokerSid
                ExpectedAgentUid = $AgentUid
                BrokerUid = $BrokerUid
                IpcGroupGid = $IpcGroupGid
            }
        }
    }
    [IO.File]::WriteAllText(
        (Join-Path $Paths.BrokerStateRoot 'appsettings.json'),
        ($settings | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
}

function Set-WindowsBrokerFirewall {
    Remove-NetFirewallRule `
        -Name $windowsFirewallRule `
        -ErrorAction SilentlyContinue
    New-NetFirewallRule `
        -Name $windowsFirewallRule `
        -DisplayName 'PitCrew support broker outbound isolation' `
        -Direction Outbound `
        -Action Block `
        -Enabled True `
        -Profile Any `
        -Service $windowsBrokerService |
        Out-Null
    $rule = Get-NetFirewallRule -Name $windowsFirewallRule
    $service = $rule | Get-NetFirewallServiceFilter
    $address = $rule | Get-NetFirewallAddressFilter
    $port = $rule | Get-NetFirewallPortFilter
    if ($rule.Enabled -ne 'True' -or
        $rule.Action -ne 'Block' -or
        $rule.Direction -ne 'Outbound' -or
        $rule.Profile -ne 'Any' -or
        (@($address.LocalAddress) -join ',') -cne 'Any' -or
        (@($address.RemoteAddress) -join ',') -cne 'Any' -or
        $port.Protocol -ne 'Any' -or
        (@($port.LocalPort) -join ',') -cne 'Any' -or
        (@($port.RemotePort) -join ',') -cne 'Any' -or
        -not $service.Service.Equals(
            $windowsBrokerService,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The broker outbound firewall rule failed structural verification.'
    }
}

function Assert-WindowsBrokerHasNoDockerAccess {
    $dockerGroup = Get-LocalGroup `
        -Name 'docker-users' `
        -ErrorAction SilentlyContinue
    if ($null -eq $dockerGroup) {
        return
    }
    $members = @(
        Get-LocalGroupMember -Group $dockerGroup -ErrorAction Stop
    )
    if ($members.Name -contains "NT SERVICE\$windowsBrokerService") {
        throw 'The broker service identity belongs to docker-users.'
    }
}

function Stop-WindowsSupportServices {
    foreach ($name in @($windowsAgentService, $windowsBrokerService)) {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            continue
        }
        try {
            if ($service.Status -ne
                [ServiceProcess.ServiceControllerStatus]::Stopped) {
                Stop-Service -Name $name -Force
                $service.WaitForStatus(
                    [ServiceProcess.ServiceControllerStatus]::Stopped,
                    [TimeSpan]::FromSeconds(30))
            }
        } finally {
            $service.Dispose()
        }
    }
}

function Get-WindowsServiceFailureDiagnostics {
    param(
        [Parameter(Mandatory)][string]$Name,
        [AllowEmptyString()][string]$StateRoot = ''
    )

    $parts = [Collections.Generic.List[string]]::new()
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        $parts.Add('status=unavailable')
    } else {
        try {
            $parts.Add("status=$($service.Status)")
        } finally {
            $service.Dispose()
        }
    }
    try {
        $metadata = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='$Name'" `
            -ErrorAction Stop
        if ($null -ne $metadata) {
            $parts.Add("state=$($metadata.State)")
            $parts.Add("win32ExitCode=$($metadata.ExitCode)")
            $parts.Add(
                "serviceSpecificExitCode=$($metadata.ServiceSpecificExitCode)")
            $parts.Add("processId=$($metadata.ProcessId)")
            $expectedStartName = "NT SERVICE\$Name"
            $parts.Add(
                "startNameExpected=$($metadata.StartName -eq $expectedStartName)")
            $parts.Add("startMode=$($metadata.StartMode)")
        }
    } catch {
        $parts.Add('metadata=unavailable')
    }
    try {
        $events = @(
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
                    [string]$_.Properties[0].Value -in @(
                        $Name,
                        "PitCrew isolated file-only diagnostics broker",
                        "PitCrew isolated support transport agent"
                    )
                }
        )
        $eventIds = @($events | Select-Object -ExpandProperty Id -Unique)
        $parts.Add(
            "serviceControlEventIds=$(if ($eventIds.Count) {
                $eventIds -join ','
            } else {
                'none'
            })")
        $eventCodes = @(
            foreach ($event in $events) {
                foreach ($property in @($event.Properties | Select-Object -Skip 1)) {
                    $value = [string]$property.Value
                    if ($value -match '^(?:0x[0-9a-fA-F]+|[0-9]+)$') {
                        $value
                    }
                }
            }
        ) | Sort-Object -Unique
        if ($eventCodes.Count -gt 0) {
            $parts.Add("serviceControlErrorCodes=$($eventCodes -join ',')")
        }
    } catch {
        $parts.Add('serviceControlEventIds=unavailable')
    }
    if (-not [string]::IsNullOrWhiteSpace($StateRoot)) {
        $statusPath = Join-Path $StateRoot 'broker-startup-status.json'
        if (Test-Path -LiteralPath $statusPath -PathType Leaf) {
            try {
                $status = Get-Content `
                    -LiteralPath $statusPath `
                    -Raw `
                    -Encoding UTF8 |
                    ConvertFrom-Json
                if ($status.schemaVersion -eq 1 -and
                    [string]$status.exceptionType -match
                        '^[A-Za-z][A-Za-z0-9]{0,127}$') {
                    $parts.Add(
                        "startupExceptionType=$($status.exceptionType)")
                }
            } catch {
                $parts.Add('startupExceptionType=unavailable')
            }
        }
    }
    return $parts -join '; '
}

function Start-WindowsSupportServices {
    $paths = Get-PlatformPaths
    & sc.exe start $windowsBrokerService | Out-Null
    $brokerStartExitCode = $LASTEXITCODE
    if ($brokerStartExitCode -ne 0) {
        $diagnostics = Get-WindowsServiceFailureDiagnostics `
            -Name $windowsBrokerService `
            -StateRoot $paths.BrokerStateRoot
        throw "The Windows support broker failed to start with SCM code $brokerStartExitCode. Bounded diagnostics: $diagnostics"
    }
    (Get-Service -Name $windowsBrokerService).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(30))
    & sc.exe start $windowsAgentService | Out-Null
    $agentStartExitCode = $LASTEXITCODE
    if ($agentStartExitCode -ne 0) {
        $diagnostics = Get-WindowsServiceFailureDiagnostics `
            -Name $windowsAgentService
        throw "The Windows support agent failed to start with SCM code $agentStartExitCode. Bounded diagnostics: $diagnostics"
    }
    (Get-Service -Name $windowsAgentService).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(30))
}

function Set-WindowsSupportStartup {
    param([Parameter(Mandatory)][bool]$Enabled)

    $startupType = if ($Enabled) {
        'Automatic'
    } else {
        'Disabled'
    }
    Set-Service -Name $windowsBrokerService -StartupType $startupType
    Set-Service -Name $windowsAgentService -StartupType $startupType
}

function Remove-WindowsService {
    param([Parameter(Mandatory)][string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }
    $service.Dispose()
    Invoke-Checked sc.exe @('delete', $Name)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $remaining = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $remaining) {
            return
        }
        $remaining.Dispose()
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Windows service '$Name' remained pending deletion."
}

function Ensure-LinuxIdentity {
    param(
        [Parameter(Mandatory)][string]$User,
        [Parameter(Mandatory)][string]$PrimaryGroup
    )

    $groupEntry = & getent group $PrimaryGroup
    if ($LASTEXITCODE -ne 0) {
        Invoke-Checked groupadd @('--system', $PrimaryGroup)
        $groupEntry = & getent group $PrimaryGroup
    }
    $accountEntry = & getent passwd $User
    if ($LASTEXITCODE -ne 0) {
        Invoke-Checked useradd @(
            '--system',
            '--gid',
            $PrimaryGroup,
            '--home-dir',
            '/nonexistent',
            '--shell',
            '/usr/sbin/nologin',
            $User
        )
        $accountEntry = & getent passwd $User
    }
    $groupFields = ([string]$groupEntry).Split(':')
    $accountFields = ([string]$accountEntry).Split(':')
    if ($groupFields.Count -lt 3 -or
        $accountFields.Count -lt 7 -or
        $accountFields[3] -ne $groupFields[2] -or
        $accountFields[5] -ne '/nonexistent' -or
        $accountFields[6] -ne '/usr/sbin/nologin') {
        throw "Existing identity '$User' does not match the product-owned service account contract."
    }
}

function Ensure-LinuxGroup {
    param([Parameter(Mandatory)][string]$Group)

    & getent group $Group | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Invoke-Checked groupadd @('--system', $Group)
    }
}

function Grant-LinuxTraverse {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$User,
        [Parameter(Mandatory)][string]$EvidenceRoot
    )

    $root = [IO.Path]::GetFullPath($EvidenceRoot)
    $current = Split-Path ([IO.Path]::GetFullPath($Path)) -Parent
    while ($current.StartsWith(
            $root,
            [StringComparison]::Ordinal)) {
        if (Test-Path -LiteralPath $current -PathType Container) {
            Invoke-Checked setfacl @('-m', "u:$User`:--x", $current)
        }
        if ($current -ceq $root) {
            break
        }
        $current = Split-Path $current -Parent
    }
}

function Deny-LinuxTreeAccess {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][uint]$AgentUid,
        [Parameter(Mandatory)][uint]$BrokerUid
    )

    Invoke-Checked setfacl @(
        '-R',
        '-m',
        "u:$AgentUid`:---,u:$BrokerUid`:---",
        $Root
    )
    $directories = @(
        Get-Item -LiteralPath $Root -Force
        Get-ChildItem `
            -LiteralPath $Root `
            -Directory `
            -Recurse `
            -Force
    )
    foreach ($directory in $directories) {
        Invoke-Checked setfacl @(
            '-m',
            "d:u:$AgentUid`:---,d:u:$BrokerUid`:---",
            $directory.FullName
        )
    }
}

function Grant-LinuxBrokerEvidence {
    param(
        [Parameter(Mandatory)][string]$ResolvedPitCrewRoot,
        [Parameter(Mandatory)][string[]]$AllowedProfiles,
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][uint]$AgentUid,
        [Parameter(Mandatory)][uint]$BrokerUid
    )

    $policy = Get-EvidencePolicy
    Grant-LinuxTraverse `
        -Path $ResolvedPitCrewRoot `
        -User ([string]$BrokerUid) `
        -EvidenceRoot ([IO.Path]::GetPathRoot($ResolvedPitCrewRoot))
    Deny-LinuxTreeAccess `
        -Root $ResolvedPitCrewRoot `
        -AgentUid $AgentUid `
        -BrokerUid $BrokerUid
    Invoke-Checked setfacl @(
        '-m',
        "u:$BrokerUid`:--x",
        $ResolvedPitCrewRoot
    )
    $stateRoot = Join-Path $ResolvedPitCrewRoot '.pitcrew-state'
    if ($policy.profileStateRootAccess -ne
        'enumerate-profile-directories-only') {
        throw 'The PitCrew profile-state access contract is unsupported.'
    }
    Invoke-Checked setfacl @(
        '-m',
        "u:$BrokerUid`:r-x",
        $stateRoot
    )
    foreach ($profile in $AllowedProfiles) {
        $profileRoot = Join-Path $stateRoot $profile
        Invoke-Checked setfacl @(
            '-m',
            "u:$BrokerUid`:--x",
            $profileRoot
        )
        foreach ($fileName in $policy.profileProjectionFiles) {
            $path = Join-Path $profileRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Invoke-Checked setfacl @(
                    '-m',
                    "u:$BrokerUid`:r--",
                    $path
                )
            }
        }
    }
    $collector = Join-Path (
        $ResolvedPitCrewRoot
    ) ([string]$policy.collectorRelativePath).Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    Grant-LinuxTraverse `
        -Path $collector `
        -User ([string]$BrokerUid) `
        -EvidenceRoot $ResolvedPitCrewRoot
    Invoke-Checked setfacl @(
        '-m',
        "u:$BrokerUid`:r--",
        $collector
    )
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        Grant-LinuxTraverse `
            -Path $connectorRoot `
            -User ([string]$BrokerUid) `
            -EvidenceRoot ([IO.Path]::GetPathRoot($connectorRoot))
        Deny-LinuxTreeAccess `
            -Root $connectorRoot `
            -AgentUid $AgentUid `
            -BrokerUid $BrokerUid
        Invoke-Checked setfacl @(
            '-m',
            "u:$BrokerUid`:--x",
            $connectorRoot
        )
    }
    if (Test-Path -LiteralPath $Paths.ConnectorHealthRoot -PathType Container) {
        Invoke-Checked setfacl @(
            '-m',
            "u:$BrokerUid`:--x",
            $Paths.ConnectorHealthRoot
        )
        foreach ($fileName in $policy.connectorHealthFiles) {
            $path = Join-Path $Paths.ConnectorHealthRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Invoke-Checked setfacl @(
                    '-m',
                    "u:$BrokerUid`:r--",
                    $path
                )
            }
        }
    }
}

function ConvertTo-SystemdArgument {
    param([Parameter(Mandatory)][string]$Value)
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Set-LinuxCurrentVersion {
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$SelectedVersion
    )

    $target = Join-Path (Join-Path $InstallRoot 'versions') $SelectedVersion
    $temporary = Join-Path $InstallRoot ".current-$([Guid]::NewGuid().ToString('N'))"
    Invoke-Checked ln @('-s', $target, $temporary)
    Invoke-Checked mv @('-Tf', $temporary, (Join-Path $InstallRoot 'current'))
}

function Write-LinuxUnits {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $agentExecutable = Join-Path (
        Join-Path $Paths.AgentInstallRoot 'current'
    ) 'PitCrew.Support.Agent.App'
    $brokerExecutable = Join-Path (
        Join-Path $Paths.BrokerInstallRoot 'current'
    ) 'PitCrew.Support.Broker.App'
    $brokerSettings = (
        Get-Content `
            -LiteralPath (
                Join-Path $Paths.BrokerStateRoot 'appsettings.json'
            ) `
            -Raw `
            -Encoding UTF8 |
            ConvertFrom-Json -Depth 10
    ).PitCrewSupport.Broker
    $agentContentRoot = ConvertTo-SystemdArgument $Paths.AgentStateRoot
    $brokerContentRoot = ConvertTo-SystemdArgument $Paths.BrokerStateRoot
    $pitCrewRoot = ConvertTo-SystemdArgument (
        [string]$brokerSettings.PitCrewRoot)
    $agentUnit = @"
[Unit]
Description=PitCrew isolated support transport agent
After=network-online.target $linuxBrokerService
Wants=network-online.target
Requires=$linuxBrokerService

[Service]
Type=simple
User=$linuxAgentUser
Group=$linuxAgentUser
SupplementaryGroups=$linuxIpcGroup
WorkingDirectory=$($Paths.AgentStateRoot)
ExecStart=$(ConvertTo-SystemdArgument $agentExecutable) --contentRoot $agentContentRoot --PitCrewSupport:Agent:SocketPath=$socketPath --PitCrewSupport:Agent:ReplayRoot=$(ConvertTo-SystemdArgument (Join-Path $Paths.AgentStateRoot 'replay'))
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateDevices=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectControlGroups=true
RestrictNamespaces=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
CapabilityBoundingSet=
AmbientCapabilities=
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6
ReadWritePaths=$agentContentRoot
UMask=0077

[Install]
WantedBy=multi-user.target
"@
    $brokerUnit = @"
[Unit]
Description=PitCrew isolated file-only diagnostics broker
After=local-fs.target

[Service]
Type=simple
User=$linuxBrokerUser
Group=$linuxIpcGroup
SupplementaryGroups=$linuxBrokerUser
WorkingDirectory=$($Paths.BrokerStateRoot)
ExecStart=$(ConvertTo-SystemdArgument $brokerExecutable) --contentRoot $brokerContentRoot
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateNetwork=true
PrivateDevices=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=tmpfs
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectControlGroups=true
RestrictNamespaces=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
CapabilityBoundingSet=
AmbientCapabilities=
RestrictAddressFamilies=AF_UNIX
IPAddressDeny=any
RuntimeDirectory=pitcrew-support
RuntimeDirectoryMode=0750
ReadWritePaths=/run/pitcrew-support $brokerContentRoot
BindReadOnlyPaths=$pitCrewRoot
UMask=0007

[Install]
WantedBy=multi-user.target
"@
    [IO.File]::WriteAllText(
        $Paths.AgentUnitPath,
        $agentUnit,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $Paths.BrokerUnitPath,
        $brokerUnit,
        [Text.UTF8Encoding]::new($false))
    Invoke-Checked systemd-analyze @(
        'verify',
        $Paths.AgentUnitPath,
        $Paths.BrokerUnitPath
    )
    Invoke-Checked systemctl @('daemon-reload')
}

function Get-SystemdProperty {
    param(
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][string]$Property
    )

    $value = & systemctl show "--property=$Property" --value $Unit
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the effective support service configuration.'
    }
    return (@($value) -join "`n").Trim()
}

function Assert-SystemdProperty {
    param(
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][string]$Property,
        [AllowEmptyString()][string]$Expected
    )

    $actual = [string](
        Get-SystemdProperty -Unit $Unit -Property $Property)
    $matches = if ($Property -ceq 'WorkingDirectory' -and
        -not [string]::IsNullOrWhiteSpace($actual)) {
        [IO.Path]::GetFullPath($actual.Trim('"')).TrimEnd('/') -ceq
            [IO.Path]::GetFullPath($Expected).TrimEnd('/')
    } else {
        $actual -ceq $Expected
    }
    if (-not $matches) {
        throw "Effective systemd property '$Property' was overridden for '$Unit'."
    }
}

function Assert-SystemdSetProperty {
    param(
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][string]$Property,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Expected
    )

    $value = [string](
        Get-SystemdProperty -Unit $Unit -Property $Property)
    $actual = if ([string]::IsNullOrWhiteSpace($value)) {
        @()
    } else {
        @(
            $value -split '\s+' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Sort-Object
        )
    }
    $expectedSorted = @($Expected | Sort-Object)
    if ((@($actual) -join "`n") -cne
        (@($expectedSorted) -join "`n")) {
        throw "Effective systemd set '$Property' was overridden for '$Unit'. Actual='$(@($actual) -join ',')'; expected='$(@($expectedSorted) -join ',')'."
    }
}

function Assert-SystemdUnitDirective {
    param(
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][string]$ExpectedFragmentPath,
        [Parameter(Mandatory)][string]$Directive,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Expected
    )

    $fragmentPath = [string](
        Get-SystemdProperty -Unit $Unit -Property 'FragmentPath')
    if ($fragmentPath -cne $ExpectedFragmentPath) {
        throw 'The effective systemd support-service fragment was overridden.'
    }
    $lines = @(
        Get-Content -LiteralPath $fragmentPath |
            Where-Object {
                $_.StartsWith(
                    "$Directive=",
                    [StringComparison]::Ordinal)
            }
    )
    if ($lines.Count -ne 1) {
        throw "Systemd directive '$Directive' is missing or duplicated for '$Unit'."
    }
    $actual = $lines[0].Substring($Directive.Length + 1)
    if ($actual -cne $Expected) {
        throw "Systemd directive '$Directive' was overridden for '$Unit'."
    }
}

function Assert-SystemdUnitDirectiveAbsent {
    param(
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][string]$ExpectedFragmentPath,
        [Parameter(Mandatory)][string]$Directive
    )

    $fragmentPath = [string](
        Get-SystemdProperty -Unit $Unit -Property 'FragmentPath')
    if ($fragmentPath -cne $ExpectedFragmentPath) {
        throw 'The effective systemd support-service fragment was overridden.'
    }
    if (@(
        Get-Content -LiteralPath $fragmentPath |
            Where-Object {
                $_.StartsWith(
                    "$Directive=",
                    [StringComparison]::Ordinal)
            }
    ).Count -ne 0) {
        throw "Systemd directive '$Directive' was unexpectedly configured for '$Unit'."
    }
}

function Assert-SystemdExecStart {
    param(
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][string]$ExpectedExecutable,
        [Parameter(Mandatory)][string]$ExpectedCommand,
        [Parameter(Mandatory)][string]$ExpectedFragmentPath
    )

    $actual = [string](
        Get-SystemdProperty -Unit $Unit -Property 'ExecStart')
    if ([string]::IsNullOrWhiteSpace($actual)) {
        $actual = [string](
            Get-SystemdProperty -Unit $Unit -Property 'ExecStartEx')
    }
    if ([string]::IsNullOrWhiteSpace($actual)) {
        $fragmentPath = [string](
            Get-SystemdProperty -Unit $Unit -Property 'FragmentPath')
        if ($fragmentPath -cne $ExpectedFragmentPath) {
            throw 'The effective systemd support-service fragment was overridden.'
        }
        $execStartLines = @(
            Get-Content -LiteralPath $fragmentPath |
                Where-Object { $_.StartsWith('ExecStart=', [StringComparison]::Ordinal) }
        )
        if ($execStartLines.Count -ne 1) {
            throw 'The effective systemd support-service command is unavailable.'
        }
        $fragmentCommand = (
            $execStartLines[0].Substring('ExecStart='.Length).Replace('"', '') -replace
                '\s+',
            ' '
        ).Trim()
        if ($fragmentCommand -cne $ExpectedCommand) {
            throw 'An effective systemd support-service command was overridden.'
        }
        return
    }
    $actualPath = $null
    $actualArguments = $null
    $pathMatched = $actual -match 'path\s*=\s*(?<path>[^ ;]+)'
    $structuredPath = if ($pathMatched) {
        $Matches.path.Trim('"')
    } else {
        $null
    }
    $argumentsMatched =
        $actual -match 'argv\[\]\s*=\s*(?<arguments>[^;]+)'
    $structuredArguments = if ($argumentsMatched) {
        $Matches.arguments.Trim()
    } else {
        $null
    }
    if ($pathMatched -and $argumentsMatched) {
        $actualPath = $structuredPath
        $actualArguments = $structuredArguments
    } else {
        $plainTokens = @(
            $actual.Trim().Split(
                ' ',
                [StringSplitOptions]::RemoveEmptyEntries) |
                ForEach-Object { $_.Trim('"') }
        )
        if ($plainTokens.Count -eq 0) {
            throw 'The effective systemd support-service command is unavailable.'
        }
        $actualPath = $plainTokens[0]
        $actualArguments = $actual.Trim()
    }
    $resolvedTarget = if (Test-Path -LiteralPath $ExpectedExecutable) {
        [IO.File]::ResolveLinkTarget(
            $ExpectedExecutable,
            $true)
    } else {
        $null
    }
    $resolvedExpected = if ($null -eq $resolvedTarget) {
        $ExpectedExecutable
    } else {
        $resolvedTarget.FullName
    }
    if ($actualPath -cne $ExpectedExecutable -and
        $actualPath -cne $resolvedExpected) {
        throw 'An effective systemd support-service command was overridden.'
    }
    $actualTokens = @(
        $actualArguments.Split(
            ' ',
            [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { $_.Trim('"') }
    )
    $expectedTokens = @(
        $ExpectedCommand.Split(
            ' ',
            [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { $_.Trim('"') }
    )
    if ($actualTokens.Count -ne $expectedTokens.Count) {
        throw 'An effective systemd support-service command was overridden.'
    }
    for ($index = 0; $index -lt $expectedTokens.Count; $index++) {
        if ($index -eq 0) {
            if ($actualTokens[$index] -cne $ExpectedExecutable -and
                $actualTokens[$index] -cne $resolvedExpected) {
                throw 'An effective systemd support-service command was overridden.'
            }
            continue
        }
        if ($actualTokens[$index] -cne $expectedTokens[$index]) {
            throw 'An effective systemd support-service command was overridden.'
        }
    }
}

function Assert-EffectiveLinuxServiceBoundary {
    param([Parameter(Mandatory)][hashtable]$Paths)

    foreach ($unit in @($linuxAgentService, $linuxBrokerService)) {
        $dropInPaths = Get-SystemdProperty `
            -Unit $unit `
            -Property 'DropInPaths'
        if (-not [string]::IsNullOrWhiteSpace($dropInPaths)) {
            throw 'Unexpected effective systemd DropInPaths were detected.'
        }
    }

    $agentExecutable = Join-Path (
        Join-Path $Paths.AgentInstallRoot 'current'
    ) 'PitCrew.Support.Agent.App'
    $brokerExecutable = Join-Path (
        Join-Path $Paths.BrokerInstallRoot 'current'
    ) 'PitCrew.Support.Broker.App'
    $agentReplayRoot = Join-Path $Paths.AgentStateRoot 'replay'
    $agentCommand =
        "$agentExecutable --contentRoot $($Paths.AgentStateRoot) --PitCrewSupport:Agent:SocketPath=$socketPath --PitCrewSupport:Agent:ReplayRoot=$agentReplayRoot"
    $brokerCommand =
        "$brokerExecutable --contentRoot $($Paths.BrokerStateRoot)"

    Assert-SystemdProperty `
        -Unit $linuxAgentService `
        -Property 'User' `
        -Expected $linuxAgentUser
    Assert-SystemdProperty `
        -Unit $linuxAgentService `
        -Property 'Group' `
        -Expected $linuxAgentUser
    Assert-SystemdSetProperty `
        -Unit $linuxAgentService `
        -Property 'SupplementaryGroups' `
        -Expected @($linuxIpcGroup)
    Assert-SystemdExecStart `
        -Unit $linuxAgentService `
        -ExpectedExecutable $agentExecutable `
        -ExpectedCommand $agentCommand `
        -ExpectedFragmentPath $Paths.AgentUnitPath
    Assert-SystemdUnitDirective `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'WorkingDirectory' `
        -Expected $Paths.AgentStateRoot
    Assert-SystemdUnitDirectiveAbsent `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'PrivateNetwork'
    Assert-SystemdUnitDirective `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'RestrictAddressFamilies' `
        -Expected 'AF_UNIX AF_INET AF_INET6'
    Assert-SystemdUnitDirectiveAbsent `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'IPAddressDeny'
    Assert-SystemdUnitDirectiveAbsent `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'IPAddressAllow'
    Assert-SystemdUnitDirective `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'ReadWritePaths' `
        -Expected (ConvertTo-SystemdArgument $Paths.AgentStateRoot)
    Assert-SystemdUnitDirectiveAbsent `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'RuntimeDirectory'
    Assert-SystemdUnitDirective `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'UMask' `
        -Expected '0077'
    Assert-SystemdUnitDirective `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'ProtectHome' `
        -Expected 'true'
    Assert-SystemdUnitDirectiveAbsent `
        -Unit $linuxAgentService `
        -ExpectedFragmentPath $Paths.AgentUnitPath `
        -Directive 'BindReadOnlyPaths'

    Assert-SystemdProperty `
        -Unit $linuxBrokerService `
        -Property 'User' `
        -Expected $linuxBrokerUser
    Assert-SystemdProperty `
        -Unit $linuxBrokerService `
        -Property 'Group' `
        -Expected $linuxIpcGroup
    Assert-SystemdSetProperty `
        -Unit $linuxBrokerService `
        -Property 'SupplementaryGroups' `
        -Expected @($linuxBrokerUser)
    Assert-SystemdExecStart `
        -Unit $linuxBrokerService `
        -ExpectedExecutable $brokerExecutable `
        -ExpectedCommand $brokerCommand `
        -ExpectedFragmentPath $Paths.BrokerUnitPath
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'WorkingDirectory' `
        -Expected $Paths.BrokerStateRoot
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'PrivateNetwork' `
        -Expected 'true'
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'RestrictAddressFamilies' `
        -Expected 'AF_UNIX'
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'IPAddressDeny' `
        -Expected 'any'
    Assert-SystemdUnitDirectiveAbsent `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'IPAddressAllow'
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'ReadWritePaths' `
        -Expected (
            "/run/pitcrew-support $(ConvertTo-SystemdArgument $Paths.BrokerStateRoot)"
        )
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'RuntimeDirectory' `
        -Expected 'pitcrew-support'
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'RuntimeDirectoryMode' `
        -Expected '0750'
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'UMask' `
        -Expected '0007'
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'ProtectHome' `
        -Expected 'tmpfs'
    $brokerSettings = (
        Get-Content `
            -LiteralPath (
                Join-Path $Paths.BrokerStateRoot 'appsettings.json'
            ) `
            -Raw `
            -Encoding UTF8 |
            ConvertFrom-Json -Depth 10
    ).PitCrewSupport.Broker
    Assert-SystemdUnitDirective `
        -Unit $linuxBrokerService `
        -ExpectedFragmentPath $Paths.BrokerUnitPath `
        -Directive 'BindReadOnlyPaths' `
        -Expected (
            ConvertTo-SystemdArgument ([string]$brokerSettings.PitCrewRoot)
        )

    foreach ($serviceBoundary in @(
        @($linuxAgentService, $Paths.AgentUnitPath),
        @($linuxBrokerService, $Paths.BrokerUnitPath)
    )) {
        $unit = $serviceBoundary[0]
        $fragmentPath = $serviceBoundary[1]
        foreach ($directive in @(
            'Environment',
            'EnvironmentFile',
            'PassEnvironment',
            'ExecCondition',
            'ExecStartPre',
            'ExecStartPost',
            'ExecReload',
            'ExecStop',
            'ExecStopPost',
            'RootDirectory',
            'RootImage',
            'BindPaths'
        )) {
            Assert-SystemdUnitDirectiveAbsent `
                -Unit $unit `
                -ExpectedFragmentPath $fragmentPath `
                -Directive $directive
        }
        foreach ($directive in @(
            'NoNewPrivileges',
            'PrivateDevices',
            'PrivateTmp',
            'ProtectKernelTunables',
            'ProtectKernelModules',
            'ProtectKernelLogs',
            'ProtectControlGroups',
            'RestrictNamespaces',
            'RestrictRealtime',
            'RestrictSUIDSGID',
            'LockPersonality'
        )) {
            Assert-SystemdUnitDirective `
                -Unit $unit `
                -ExpectedFragmentPath $fragmentPath `
                -Directive $directive `
                -Expected 'true'
        }
        Assert-SystemdUnitDirective `
            -Unit $unit `
            -ExpectedFragmentPath $fragmentPath `
            -Directive 'ProtectSystem' `
            -Expected 'strict'
        Assert-SystemdUnitDirective `
            -Unit $unit `
            -ExpectedFragmentPath $fragmentPath `
            -Directive 'CapabilityBoundingSet' `
            -Expected ''
        Assert-SystemdUnitDirective `
            -Unit $unit `
            -ExpectedFragmentPath $fragmentPath `
            -Directive 'AmbientCapabilities' `
            -Expected ''
    }
}

function Stop-LinuxSupportServices {
    foreach ($service in @($linuxAgentService, $linuxBrokerService)) {
        & systemctl stop $service | Out-Null
        if ($LASTEXITCODE -notin @(0, 5)) {
            throw "Could not stop '$service'."
        }
    }
}

function Wait-LinuxSupportServiceActive {
    param(
        [Parameter(Mandatory)][string]$Service,
        [int]$TimeoutSeconds = 10
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $activeSince = $null
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $activeState = [string](
            Get-SystemdProperty -Unit $Service -Property 'ActiveState')
        if ($activeState -ceq 'active') {
            if ($null -eq $activeSince) {
                $activeSince = $stopwatch.Elapsed
            } elseif (
                ($stopwatch.Elapsed - $activeSince).TotalSeconds -ge 1
            ) {
                return
            }
        } else {
            $activeSince = $null
        }
        Start-Sleep -Milliseconds 100
    }

    $diagnostics = @(
        foreach ($property in @(
            'ActiveState',
            'SubState',
            'Result',
            'ExecMainCode',
            'ExecMainStatus'
        )) {
            $value = [string](
                Get-SystemdProperty -Unit $Service -Property $property)
            "$property=$value"
        }
    ) -join ';'
    throw "Linux support service '$Service' did not stabilize as active. Bounded diagnostics: $diagnostics"
}

function Start-LinuxSupportServices {
    Invoke-Checked systemctl @('start', $linuxBrokerService)
    Invoke-Checked systemctl @('start', $linuxAgentService)
    foreach ($service in @($linuxBrokerService, $linuxAgentService)) {
        Wait-LinuxSupportServiceActive -Service $service
    }
}

function Configure-WindowsVersion {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$SelectedVersion
    )

    $agentExecutable = Join-Path (
        Join-Path (
            Join-Path $Paths.AgentInstallRoot 'versions'
        ) $SelectedVersion
    ) 'PitCrew.Support.Agent.App.exe'
    $brokerExecutable = Join-Path (
        Join-Path (
            Join-Path $Paths.BrokerInstallRoot 'versions'
        ) $SelectedVersion
    ) 'PitCrew.Support.Broker.App.exe'
    Set-WindowsServiceDefinition `
        -Name $windowsAgentService `
        -DisplayName 'PitCrew isolated support transport agent' `
        -Executable $agentExecutable `
        -Arguments "--contentRoot `"$($Paths.AgentStateRoot)`" --PitCrewSupport:Agent:PipeName=$pipeName --PitCrewSupport:Agent:ReplayRoot=`"$(Join-Path $Paths.AgentStateRoot 'replay')`"" `
        -BundleExtractRoot (Join-Path $Paths.AgentStateRoot 'bundle') `
        -RequiredPrivileges @('SeChangeNotifyPrivilege')
    Set-WindowsServiceDefinition `
        -Name $windowsBrokerService `
        -DisplayName 'PitCrew isolated file-only diagnostics broker' `
        -Executable $brokerExecutable `
        -Arguments "--contentRoot `"$($Paths.BrokerStateRoot)`"" `
        -BundleExtractRoot (Join-Path $Paths.BrokerStateRoot 'bundle') `
        -RequiredPrivileges @(
            'SeChangeNotifyPrivilege',
            'SeImpersonatePrivilege'
        )
    Invoke-Checked sc.exe @(
        'config',
        $windowsAgentService,
        'depend=',
        $windowsBrokerService
    )
    Set-WindowsBrokerFirewall
}

function Configure-LinuxVersion {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$SelectedVersion
    )

    Set-LinuxCurrentVersion `
        -InstallRoot $Paths.AgentInstallRoot `
        -SelectedVersion $SelectedVersion
    Set-LinuxCurrentVersion `
        -InstallRoot $Paths.BrokerInstallRoot `
        -SelectedVersion $SelectedVersion
    Write-LinuxUnits -Paths $Paths
}

function Initialize-WindowsInstallation {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$SelectedVersion,
        [Parameter(Mandatory)][string]$ResolvedPitCrewRoot,
        [Parameter(Mandatory)][string[]]$AllowedProfiles
    )

    Configure-WindowsVersion `
        -Paths $Paths `
        -SelectedVersion $SelectedVersion
    Assert-WindowsBrokerHasNoDockerAccess
    $agentSid = Get-WindowsServiceSid -ServiceName $windowsAgentService
    $brokerSid = Get-WindowsServiceSid -ServiceName $windowsBrokerService
    Grant-WindowsServiceParentTraversal `
        -Paths $Paths `
        -AgentSid $agentSid `
        -BrokerSid $brokerSid
    Set-WindowsProtectedDirectoryAcl `
        -Path $Paths.AgentInstallRoot `
        -ServiceSid $agentSid `
        -ServiceRights 'RX'
    Set-WindowsProtectedDirectoryAcl `
        -Path $Paths.BrokerInstallRoot `
        -ServiceSid $brokerSid `
        -ServiceRights 'RX'
    Set-WindowsProtectedDirectoryAcl `
        -Path $Paths.AgentStateRoot `
        -ServiceSid $agentSid `
        -ServiceRights 'F'
    Set-WindowsProtectedDirectoryAcl `
        -Path $Paths.BrokerStateRoot `
        -ServiceSid $brokerSid `
        -ServiceRights 'F'
    Write-BrokerSettings `
        -Paths $Paths `
        -ResolvedPitCrewRoot $ResolvedPitCrewRoot `
        -AllowedProfiles $AllowedProfiles `
        -AgentSid $agentSid `
        -BrokerSid $brokerSid `
        -AgentUid $null `
        -BrokerUid $null `
        -IpcGroupGid $null
    Grant-WindowsBrokerEvidence `
        -ResolvedPitCrewRoot $ResolvedPitCrewRoot `
        -AllowedProfiles $AllowedProfiles `
        -BrokerSid $brokerSid `
        -AgentSid $agentSid `
        -Paths $Paths
    $installedSettings = Get-Content `
        -LiteralPath (Join-Path $Paths.BrokerStateRoot 'appsettings.json') `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
    Assert-EvidenceFilesReadable `
        -Paths $Paths `
        -Settings $installedSettings.PitCrewSupport.Broker
}

function Initialize-LinuxInstallation {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$SelectedVersion,
        [Parameter(Mandatory)][string]$ResolvedPitCrewRoot,
        [Parameter(Mandatory)][string[]]$AllowedProfiles
    )

    Ensure-LinuxGroup -Group $linuxIpcGroup
    Ensure-LinuxGroup -Group $linuxAgentUser
    Ensure-LinuxGroup -Group $linuxBrokerUser
    Ensure-LinuxIdentity `
        -User $linuxAgentUser `
        -PrimaryGroup $linuxAgentUser
    Ensure-LinuxIdentity `
        -User $linuxBrokerUser `
        -PrimaryGroup $linuxBrokerUser
    Invoke-Checked usermod @('-G', $linuxIpcGroup, $linuxAgentUser)
    Invoke-Checked usermod @('-G', $linuxIpcGroup, $linuxBrokerUser)
    foreach ($root in @(
        $Paths.AgentInstallRoot,
        $Paths.AgentStateRoot,
        $Paths.BrokerInstallRoot,
        $Paths.BrokerStateRoot,
        $Paths.InstallerStateRoot
    )) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
    }
    Invoke-Checked chown @(
        '-R',
        "root`:$linuxAgentUser",
        $Paths.AgentInstallRoot
    )
    Invoke-Checked chmod @(
        '-R',
        'u=rwX,g=rX,o=',
        $Paths.AgentInstallRoot
    )
    Invoke-Checked chown @(
        '-R',
        "$linuxAgentUser`:$linuxAgentUser",
        $Paths.AgentStateRoot
    )
    Invoke-Checked chmod @(
        '-R',
        'u=rwX,go=',
        $Paths.AgentStateRoot
    )
    Invoke-Checked chown @(
        '-R',
        "root`:$linuxBrokerUser",
        $Paths.BrokerInstallRoot
    )
    Invoke-Checked chmod @(
        '-R',
        'u=rwX,g=rX,o=',
        $Paths.BrokerInstallRoot
    )
    Invoke-Checked chown @(
        '-R',
        "$linuxBrokerUser`:$linuxBrokerUser",
        $Paths.BrokerStateRoot
    )
    Invoke-Checked chmod @(
        '-R',
        'u=rwX,go=',
        $Paths.BrokerStateRoot
    )
    Invoke-Checked chown @(
        '-R',
        'root:root',
        $Paths.InstallerStateRoot
    )
    Invoke-Checked chmod @(
        '-R',
        'u=rwX,go=',
        $Paths.InstallerStateRoot
    )
    $agentUid = [uint](& id -u $linuxAgentUser)
    $brokerUid = [uint](& id -u $linuxBrokerUser)
    $ipcGroupGid = [uint](& getent group $linuxIpcGroup).Split(':')[2]
    Write-BrokerSettings `
        -Paths $Paths `
        -ResolvedPitCrewRoot $ResolvedPitCrewRoot `
        -AllowedProfiles $AllowedProfiles `
        -AgentSid '' `
        -BrokerSid '' `
        -AgentUid $agentUid `
        -BrokerUid $brokerUid `
        -IpcGroupGid $ipcGroupGid
    Invoke-Checked chown @(
        "$linuxBrokerUser`:$linuxBrokerUser",
        (Join-Path $Paths.BrokerStateRoot 'appsettings.json')
    )
    Invoke-Checked chmod @(
        '600',
        (Join-Path $Paths.BrokerStateRoot 'appsettings.json')
    )
    if (Test-Path `
            -LiteralPath (
                Get-LinuxEvidenceMetadataPath -Paths $Paths
            ) `
            -PathType Leaf) {
        Assert-LinuxEvidenceMetadataExact `
            -Paths $Paths `
            -PitCrewRoot $ResolvedPitCrewRoot `
            -Profiles $AllowedProfiles
    }
    Grant-LinuxBrokerEvidence `
        -ResolvedPitCrewRoot $ResolvedPitCrewRoot `
        -AllowedProfiles $AllowedProfiles `
        -Paths $Paths `
        -AgentUid $agentUid `
        -BrokerUid $brokerUid
    Initialize-LinuxEvidenceMetadataContract `
        -Paths $Paths `
        -PitCrewRoot $ResolvedPitCrewRoot `
        -Profiles $AllowedProfiles
    $installedSettings = Get-Content `
        -LiteralPath (Join-Path $Paths.BrokerStateRoot 'appsettings.json') `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
    Assert-EvidenceFilesReadable `
        -Paths $Paths `
        -Settings $installedSettings.PitCrewSupport.Broker
    Configure-LinuxVersion `
        -Paths $Paths `
        -SelectedVersion $SelectedVersion
    Assert-EffectiveLinuxServiceBoundary -Paths $Paths
}

function Copy-AgentSettings {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][bool]$Required
    )

    if ([string]::IsNullOrWhiteSpace($AgentSettingsPath)) {
        if ($Required -and
            -not (Test-Path `
                -LiteralPath (Join-Path $Paths.AgentStateRoot 'appsettings.json') `
                -PathType Leaf)) {
            throw 'Initial installation requires an existing protected agent settings file.'
        }
        return
    }
    $source = (Resolve-Path -LiteralPath $AgentSettingsPath).Path
    $destination = Join-Path $Paths.AgentStateRoot 'appsettings.json'
    if ($source.Equals(
            [IO.Path]::GetFullPath($destination),
            $(if ($IsWindows) {
                [StringComparison]::OrdinalIgnoreCase
            } else {
                [StringComparison]::Ordinal
            }))) {
        return
    }
    Copy-Item `
        -LiteralPath $source `
        -Destination $destination `
        -Force
}

function Invoke-InstallOrUpdate {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [AllowNull()][object]$Manifest,
        [Parameter(Mandatory)][bool]$IsUpdate
    )

    Assert-InstallInputs `
        -RequiresSettings (-not $IsUpdate) `
        -Paths $Paths
    if ($IsUpdate -and
        -not [string]::IsNullOrWhiteSpace($AgentSettingsPath)) {
        throw 'Support identity settings are not changed by binary updates.'
    }
    $resolvedPitCrewRoot = (Resolve-Path -LiteralPath $PitCrewRoot).Path
    if ($IsUpdate) {
        $installedSettings = Get-Content `
            -LiteralPath (
                Join-Path $Paths.BrokerStateRoot 'appsettings.json'
            ) `
            -Raw `
            -Encoding UTF8 |
            ConvertFrom-Json -Depth 10
        $installedBroker = $installedSettings.PitCrewSupport.Broker
        $pathComparison = if ($IsWindows) {
            [StringComparison]::OrdinalIgnoreCase
        } else {
            [StringComparison]::Ordinal
        }
        $installedProfiles = @(
            ([string]$installedBroker.AllowedProfiles).Split(',') |
                Sort-Object
        )
        $requestedProfiles = @($Profiles | Sort-Object)
        if (-not $resolvedPitCrewRoot.Equals(
                [string]$installedBroker.PitCrewRoot,
                $pathComparison) -or
            @(Compare-Object `
                -ReferenceObject $installedProfiles `
                -DifferenceObject $requestedProfiles `
                -CaseSensitive).Count -ne 0) {
            throw 'Binary updates cannot change the locally selected PitCrew root or profiles.'
        }
    }
    $previousVersion = if ($null -eq $Manifest) {
        ''
    } else {
        [string]$Manifest.currentVersion
    }
    if ($IsUpdate) {
        Assert-InstalledSupportVersion `
            -Paths $Paths `
            -InstalledVersion $previousVersion
    }
    $wasEnabled = $null -eq $Manifest -or [bool]$Manifest.enabled
    $brokerSettingsPath = Join-Path $Paths.BrokerStateRoot 'appsettings.json'
    $previousBrokerSettings = if (
        Test-Path -LiteralPath $brokerSettingsPath -PathType Leaf
    ) {
        [IO.File]::ReadAllBytes($brokerSettingsPath)
    } else {
        $null
    }
    try {
        $staged = Stage-Release -Paths $Paths
    } catch {
        if (-not $IsUpdate) {
            Remove-Item `
                -LiteralPath `
                    $Paths.AgentInstallRoot,
                    $Paths.BrokerInstallRoot,
                    $Paths.BrokerStateRoot,
                    $Paths.InstallerStateRoot `
                -Recurse `
                -Force `
                -ErrorAction SilentlyContinue
        }
        throw
    }
    try {
        if ($IsWindows) {
            Stop-WindowsSupportServices
        } else {
            Stop-LinuxSupportServices
        }
        if ($IsUpdate) {
            New-Item `
                -ItemType Directory `
                -Path $Paths.AgentStateRoot, $Paths.BrokerStateRoot `
                -Force |
                Out-Null
        } else {
            Protect-StateRootsForInstaller -Paths $Paths
        }
        Copy-AgentSettings -Paths $Paths -Required (-not $IsUpdate)
        if ($IsWindows) {
            Initialize-WindowsInstallation `
                -Paths $Paths `
                -SelectedVersion $Version `
                -ResolvedPitCrewRoot $resolvedPitCrewRoot `
                -AllowedProfiles $Profiles
        } else {
            Initialize-LinuxInstallation `
                -Paths $Paths `
                -SelectedVersion $Version `
                -ResolvedPitCrewRoot $resolvedPitCrewRoot `
                -AllowedProfiles $Profiles
        }
        if ($wasEnabled) {
            if ($IsWindows) {
                Start-WindowsSupportServices
            } else {
                Invoke-Checked systemctl @(
                    'enable',
                    $linuxBrokerService,
                    $linuxAgentService
                )
                Start-LinuxSupportServices
            }
        } elseif ($IsWindows) {
            Set-WindowsSupportStartup -Enabled $false
        } else {
            Invoke-Checked systemctl @(
                'disable',
                $linuxBrokerService,
                $linuxAgentService
            )
        }
        $keepVersions = [Collections.Generic.List[string]]::new()
        $keepVersions.Add($Version)
        if (-not [string]::IsNullOrWhiteSpace($previousVersion)) {
            $keepVersions.Add($previousVersion)
        }
        Remove-ObsoleteSupportVersions `
            -Paths $Paths `
            -KeepVersions $keepVersions
        Write-InstallManifest `
            -Paths $Paths `
            -CurrentVersion $Version `
            -PreviousVersion $previousVersion `
            -Enabled $wasEnabled
        Remove-Item `
            -LiteralPath (
                Join-Path $Paths.AgentStateRoot 'identity-preserved.json'
            ) `
            -Force `
            -ErrorAction SilentlyContinue
    } catch {
        if (-not [string]::IsNullOrWhiteSpace($previousVersion)) {
            if ($null -ne $previousBrokerSettings) {
                [IO.File]::WriteAllBytes(
                    $brokerSettingsPath,
                    $previousBrokerSettings)
            }
            if ($IsWindows) {
                Initialize-WindowsInstallation `
                    -Paths $Paths `
                    -SelectedVersion $previousVersion `
                    -ResolvedPitCrewRoot $resolvedPitCrewRoot `
                    -AllowedProfiles $Profiles
                if ($wasEnabled) {
                    Start-WindowsSupportServices
                } else {
                    Set-WindowsSupportStartup -Enabled $false
                }
            } else {
                Initialize-LinuxInstallation `
                    -Paths $Paths `
                    -SelectedVersion $previousVersion `
                    -ResolvedPitCrewRoot $resolvedPitCrewRoot `
                    -AllowedProfiles $Profiles
                if ($wasEnabled) {
                    Start-LinuxSupportServices
                }
            }
        } else {
            $failedBrokerSettings = if (
                Test-Path -LiteralPath $brokerSettingsPath -PathType Leaf
            ) {
                (
                    Get-Content `
                        -LiteralPath $brokerSettingsPath `
                        -Raw `
                        -Encoding UTF8 |
                        ConvertFrom-Json -Depth 10
                ).PitCrewSupport.Broker
            } else {
                $null
            }
            if ($IsWindows) {
                Stop-WindowsSupportServices
                if ($null -ne $failedBrokerSettings) {
                    Revoke-WindowsEvidenceAccess `
                        -Paths $Paths `
                        -Settings $failedBrokerSettings
                    Revoke-WindowsServiceParentTraversal `
                        -Paths $Paths `
                        -AgentSid ([string]$failedBrokerSettings.ExpectedAgentSid) `
                        -BrokerSid ([string]$failedBrokerSettings.BrokerServiceSid)
                }
                if (Test-Path `
                        -LiteralPath (
                            Join-Path $Paths.AgentStateRoot 'appsettings.json'
                        ) `
                        -PathType Leaf) {
                    Preserve-AgentIdentityState -Paths $Paths
                }
                Remove-NetFirewallRule `
                    -Name $windowsFirewallRule `
                    -ErrorAction SilentlyContinue
                Remove-WindowsService -Name $windowsAgentService
                Remove-WindowsService -Name $windowsBrokerService
            } else {
                Stop-LinuxSupportServices
                if ($null -ne $failedBrokerSettings) {
                    Revoke-LinuxEvidenceAccess `
                        -Paths $Paths `
                        -Settings $failedBrokerSettings
                }
                if (Test-Path `
                        -LiteralPath (
                            Join-Path $Paths.AgentStateRoot 'appsettings.json'
                        ) `
                        -PathType Leaf) {
                    Preserve-AgentIdentityState -Paths $Paths
                }
                Remove-Item `
                    -LiteralPath $Paths.AgentUnitPath, $Paths.BrokerUnitPath `
                    -Force `
                    -ErrorAction SilentlyContinue
                Invoke-Checked systemctl @('daemon-reload')
                Remove-LinuxProductIdentities
            }
            Remove-Item `
                -LiteralPath `
                    $Paths.AgentInstallRoot,
                    $Paths.BrokerInstallRoot,
                    $Paths.BrokerStateRoot,
                    $Paths.InstallerStateRoot `
                -Recurse `
                -Force `
                -ErrorAction SilentlyContinue
        }
        Remove-Item `
            -LiteralPath $staged.AgentVersionRoot, $staged.BrokerVersionRoot `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
        throw
    }
}

function Invoke-Enable {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Manifest
    )

    if ([bool]$Manifest.enabled) {
        return
    }
    try {
        if ($IsWindows) {
            Set-WindowsSupportStartup -Enabled $true
            Start-WindowsSupportServices
        } else {
            Invoke-Checked systemctl @(
                'enable',
                $linuxBrokerService,
                $linuxAgentService
            )
            Start-LinuxSupportServices
        }
    } catch {
        if ($IsWindows) {
            Stop-WindowsSupportServices
            Set-WindowsSupportStartup -Enabled $false
        } else {
            & systemctl disable --now `
                $linuxAgentService `
                $linuxBrokerService |
                Out-Null
        }
        throw
    }
    Write-InstallManifest `
        -Paths $Paths `
        -CurrentVersion ([string]$Manifest.currentVersion) `
        -PreviousVersion ([string]$Manifest.previousVersion) `
        -Enabled $true
}

function Invoke-Disable {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Manifest
    )

    if (-not [bool]$Manifest.enabled) {
        return
    }
    try {
        if ($IsWindows) {
            Stop-WindowsSupportServices
            Set-WindowsSupportStartup -Enabled $false
        } else {
            Invoke-Checked systemctl @(
                'disable',
                '--now',
                $linuxAgentService,
                $linuxBrokerService
            )
        }
    } catch {
        if ($IsWindows) {
            Set-WindowsSupportStartup -Enabled $true
            Start-WindowsSupportServices
        } else {
            Invoke-Checked systemctl @(
                'enable',
                $linuxBrokerService,
                $linuxAgentService
            )
            Start-LinuxSupportServices
        }
        throw
    }
    Write-InstallManifest `
        -Paths $Paths `
        -CurrentVersion ([string]$Manifest.currentVersion) `
        -PreviousVersion ([string]$Manifest.previousVersion) `
        -Enabled $false
}

function Invoke-Rollback {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Manifest
    )

    $previousVersion = [string]$Manifest.previousVersion
    if ([string]::IsNullOrWhiteSpace($previousVersion)) {
        throw 'No rollback version is available.'
    }
    Assert-InstalledSupportVersion `
        -Paths $Paths `
        -InstalledVersion $previousVersion
    $currentVersion = [string]$Manifest.currentVersion
    try {
        if ($IsWindows) {
            Stop-WindowsSupportServices
            Configure-WindowsVersion `
                -Paths $Paths `
                -SelectedVersion $previousVersion
            if ([bool]$Manifest.enabled) {
                Start-WindowsSupportServices
            } else {
                Set-WindowsSupportStartup -Enabled $false
            }
        } else {
            Stop-LinuxSupportServices
            Configure-LinuxVersion `
                -Paths $Paths `
                -SelectedVersion $previousVersion
            if ([bool]$Manifest.enabled) {
                Start-LinuxSupportServices
            }
        }
    } catch {
        if ($IsWindows) {
            Stop-WindowsSupportServices
            Configure-WindowsVersion `
                -Paths $Paths `
                -SelectedVersion $currentVersion
            if ([bool]$Manifest.enabled) {
                Start-WindowsSupportServices
            } else {
                Set-WindowsSupportStartup -Enabled $false
            }
        } else {
            Stop-LinuxSupportServices
            Configure-LinuxVersion `
                -Paths $Paths `
                -SelectedVersion $currentVersion
            if ([bool]$Manifest.enabled) {
                Start-LinuxSupportServices
            }
        }
        throw
    }
    Write-InstallManifest `
        -Paths $Paths `
        -CurrentVersion $previousVersion `
        -PreviousVersion $currentVersion `
        -Enabled ([bool]$Manifest.enabled)
    Remove-ObsoleteSupportVersions `
        -Paths $Paths `
        -KeepVersions @($previousVersion, $currentVersion)
}

function Revoke-WindowsEvidenceAccess {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Settings
    )

    $brokerSid = [string]$Settings.BrokerServiceSid
    $agentSid = [string]$Settings.ExpectedAgentSid
    $roots = [Collections.Generic.List[string]]::new()
    $roots.Add([string]$Settings.PitCrewRoot)
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        $roots.Add($connectorRoot)
    }
    foreach ($root in $roots) {
        foreach ($accessType in @('/remove:g', '/remove:d')) {
            Invoke-Checked icacls.exe @(
                $root,
                $accessType,
                "*$brokerSid",
                "*$agentSid",
                '/T',
                '/C'
            )
        }
        $items = @(
            Get-Item -LiteralPath $root -Force
            Get-ChildItem `
                -LiteralPath $root `
                -Recurse `
                -Force `
                -ErrorAction Stop
        )
        foreach ($item in $items) {
            $remaining = @(
                (Get-Acl -LiteralPath $item.FullName).GetAccessRules(
                    $true,
                    $true,
                    [Security.Principal.SecurityIdentifier]) |
                    Where-Object {
                        $_.IdentityReference.Value -in @(
                            $brokerSid,
                            $agentSid
                        )
                    }
            )
            if ($remaining.Count -gt 0) {
                throw 'Product evidence ACE revocation was incomplete.'
            }
        }
    }
}

function Revoke-LinuxEvidenceAccess {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Settings
    )

    $roots = [Collections.Generic.List[string]]::new()
    $roots.Add([string]$Settings.PitCrewRoot)
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        $roots.Add($connectorRoot)
    }
    $agentUid = [string]$Settings.ExpectedAgentUid
    $brokerUid = [string]$Settings.BrokerUid
    if ($agentUid -notmatch '^[0-9]+$' -or
        $brokerUid -notmatch '^[0-9]+$') {
        throw 'Installed Linux support identity metadata is invalid.'
    }
    foreach ($root in $roots) {
        Invoke-Checked setfacl @(
            '-R',
            '-x',
            "u:$agentUid,u:$brokerUid",
            $root
        )
        $directories = @(
            Get-Item -LiteralPath $root -Force
            Get-ChildItem `
                -LiteralPath $root `
                -Directory `
                -Recurse `
                -Force `
                -ErrorAction Stop
        )
        foreach ($directory in $directories) {
            Invoke-Checked setfacl @(
                '-x',
                "d:u:$agentUid,d:u:$brokerUid",
                $directory.FullName
            )
        }
        $items = @(
            Get-Item -LiteralPath $root -Force
            Get-ChildItem `
                -LiteralPath $root `
                -Recurse `
                -Force `
                -ErrorAction Stop
        )
        foreach ($item in $items) {
            $entries = Get-LinuxAclEntries -Path $item.FullName
            if ($entries.ContainsKey(
                    "user:$([string]$Settings.ExpectedAgentUid)") -or
                $entries.ContainsKey(
                    "user:$([string]$Settings.BrokerUid)") -or
                $entries.ContainsKey(
                    "default:user:$([string]$Settings.ExpectedAgentUid)") -or
                $entries.ContainsKey(
                    "default:user:$([string]$Settings.BrokerUid)")) {
                throw 'Product evidence ACL revocation was incomplete.'
            }
        }
    }
}

function Assert-LinuxProductIdentityRemovable {
    param(
        [Parameter(Mandatory)][string]$User,
        [Parameter(Mandatory)][string]$PrimaryGroup
    )

    $account = & getent passwd $User
    if ($LASTEXITCODE -ne 0) {
        return
    }
    $group = & getent group $PrimaryGroup
    if ($LASTEXITCODE -ne 0) {
        throw 'A product service identity no longer matches its installation contract.'
    }
    $accountFields = ([string]$account).Split(':')
    $groupFields = ([string]$group).Split(':')
    if ($accountFields.Count -lt 7 -or
        $groupFields.Count -lt 3 -or
        $accountFields[3] -ne $groupFields[2] -or
        $accountFields[5] -ne '/nonexistent' -or
        $accountFields[6] -ne '/usr/sbin/nologin') {
        throw 'A product service identity no longer matches its installation contract.'
    }
}

function Remove-LinuxProductIdentities {
    Assert-LinuxProductGroupsRemovable
    foreach ($user in @($linuxAgentUser, $linuxBrokerUser)) {
        & getent passwd $user | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Invoke-Checked userdel @($user)
        }
    }
    foreach ($group in @(
        $linuxAgentUser,
        $linuxBrokerUser,
        $linuxIpcGroup
    )) {
        & getent group $group | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Invoke-Checked groupdel @($group)
        }
    }
}

function Assert-LinuxProductGroupsRemovable {
    Assert-LinuxProductIdentityRemovable `
        -User $linuxAgentUser `
        -PrimaryGroup $linuxAgentUser
    Assert-LinuxProductIdentityRemovable `
        -User $linuxBrokerUser `
        -PrimaryGroup $linuxBrokerUser

    $passwdEntries = @(& getent passwd)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect Linux account primary-group ownership.'
    }
    $contracts = @(
        @{
            Group = $linuxAgentUser
            SupplementaryMembers = @()
            PrimaryUsers = @($linuxAgentUser)
        },
        @{
            Group = $linuxBrokerUser
            SupplementaryMembers = @()
            PrimaryUsers = @($linuxBrokerUser)
        },
        @{
            Group = $linuxIpcGroup
            SupplementaryMembers = @(
                $linuxAgentUser,
                $linuxBrokerUser
            )
            PrimaryUsers = @()
        }
    )
    foreach ($contract in $contracts) {
        $groupEntry = & getent group $contract.Group
        if ($LASTEXITCODE -ne 0) {
            throw 'A product service group is unavailable.'
        }
        $groupFields = ([string]$groupEntry).Split(':')
        if ($groupFields.Count -lt 4) {
            throw 'A product service group is invalid.'
        }
        $members = @(
            $groupFields[3].Split(
                ',',
                [StringSplitOptions]::RemoveEmptyEntries) |
                Sort-Object
        )
        $expectedMembers = @(
            $contract.SupplementaryMembers |
                Sort-Object
        )
        if ((@($members) -join ',') -cne
            (@($expectedMembers) -join ',')) {
            throw 'A product service group contains an external supplementary member.'
        }
        $primaryUsers = @(
            foreach ($entry in $passwdEntries) {
                $fields = ([string]$entry).Split(':')
                if ($fields.Count -ge 4 -and
                    $fields[3] -ceq $groupFields[2]) {
                    $fields[0]
                }
            }
        ) | Sort-Object
        $expectedPrimaryUsers = @(
            $contract.PrimaryUsers |
                Sort-Object
        )
        if ((@($primaryUsers) -join ',') -cne
            (@($expectedPrimaryUsers) -join ',')) {
            throw 'A product service group is an external account primary group.'
        }
    }
}

function Preserve-AgentIdentityState {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $settingsPath = Join-Path $Paths.AgentStateRoot 'appsettings.json'
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        throw 'The protected support identity settings are missing.'
    }
    [IO.File]::WriteAllText(
        (Join-Path $Paths.AgentStateRoot 'identity-preserved.json'),
        '{"schemaVersion":1,"identityHandling":"PreserveKeys"}',
        [Text.UTF8Encoding]::new($false))
    if ($IsWindows) {
        Protect-StateRootsForInstaller -Paths $Paths
    } else {
        Invoke-Checked chown @(
            '-R',
            'root:root',
            $Paths.AgentStateRoot
        )
        Invoke-Checked chmod @(
            '-R',
            'u=rwX,go=',
            $Paths.AgentStateRoot
        )
    }
}

function Invoke-Uninstall {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $settings = Get-Content `
        -LiteralPath (Join-Path $Paths.BrokerStateRoot 'appsettings.json') `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
    $brokerSettings = $settings.PitCrewSupport.Broker
    if ($IsLinux) {
        Assert-LinuxProductGroupsRemovable
    }
    if ($IsWindows) {
        Stop-WindowsSupportServices
        Revoke-WindowsEvidenceAccess `
            -Paths $Paths `
            -Settings $brokerSettings
        Revoke-WindowsServiceParentTraversal `
            -Paths $Paths `
            -AgentSid ([string]$brokerSettings.ExpectedAgentSid) `
            -BrokerSid ([string]$brokerSettings.BrokerServiceSid)
        Preserve-AgentIdentityState -Paths $Paths
        Remove-NetFirewallRule `
            -Name $windowsFirewallRule `
            -ErrorAction SilentlyContinue
        Remove-WindowsService -Name $windowsAgentService
        Remove-WindowsService -Name $windowsBrokerService
    } else {
        & systemctl disable --now $linuxAgentService $linuxBrokerService |
            Out-Null
        if ($LASTEXITCODE -notin @(0, 5)) {
            throw 'Could not disable the support services.'
        }
        Revoke-LinuxEvidenceAccess `
            -Paths $Paths `
            -Settings $brokerSettings
        Preserve-AgentIdentityState -Paths $Paths
        Remove-Item `
            -LiteralPath $Paths.AgentUnitPath, $Paths.BrokerUnitPath `
            -Force `
            -ErrorAction SilentlyContinue
        Invoke-Checked systemctl @('daemon-reload')
        Remove-LinuxProductIdentities
    }
    Remove-Item `
        -LiteralPath $Paths.AgentInstallRoot, $Paths.BrokerInstallRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
    Remove-Item `
        -LiteralPath $Paths.BrokerStateRoot, $Paths.InstallerStateRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
}

function Get-WindowsNormalizedRights {
    param(
        [Parameter(Mandatory)]
        [Security.AccessControl.FileSystemRights]$Rights
    )

    return [int64]$Rights -band (
        -bnot [int64][Security.AccessControl.FileSystemRights]::Synchronize)
}

function Add-WindowsExpectedEvidenceAce {
    param(
        [Parameter(Mandatory)]
        [Collections.Generic.Dictionary[string, long]]$Expected,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]
        [Security.AccessControl.FileSystemRights]$Rights
    )

    $Expected[[IO.Path]::GetFullPath($Path)] =
        Get-WindowsNormalizedRights -Rights $Rights
}

function Get-WindowsExpectedEvidenceAces {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$PitCrewRoot,
        [Parameter(Mandatory)][string[]]$Profiles
    )

    $expected = [Collections.Generic.Dictionary[string, long]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $traverseRights =
        [Security.AccessControl.FileSystemRights]::ExecuteFile -bor
        [Security.AccessControl.FileSystemRights]::ReadAttributes
    $enumerateRights =
        $traverseRights -bor
        [Security.AccessControl.FileSystemRights]::ReadData
    Add-WindowsExpectedEvidenceAce `
        -Expected $expected `
        -Path $PitCrewRoot `
        -Rights $traverseRights
    $policy = Get-EvidencePolicy
    foreach ($sentinel in $policy.installationSentinels) {
        Add-WindowsExpectedEvidenceAce `
            -Expected $expected `
            -Path (Join-Path $PitCrewRoot $sentinel) `
            -Rights (
                [Security.AccessControl.FileSystemRights]::ReadAttributes
            )
    }
    $stateRoot = Join-Path $PitCrewRoot '.pitcrew-state'
    Add-WindowsExpectedEvidenceAce `
        -Expected $expected `
        -Path $stateRoot `
        -Rights $enumerateRights
    foreach ($profile in $Profiles) {
        $profileRoot = Join-Path $stateRoot $profile
        Add-WindowsExpectedEvidenceAce `
            -Expected $expected `
            -Path $profileRoot `
            -Rights $traverseRights
        foreach ($fileName in $policy.profileProjectionFiles) {
            $path = Join-Path $profileRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Add-WindowsExpectedEvidenceAce `
                    -Expected $expected `
                    -Path $path `
                    -Rights (
                        [Security.AccessControl.FileSystemRights]::Read
                    )
            }
        }
    }
    $collector = Join-Path (
        $PitCrewRoot
    ) ([string]$policy.collectorRelativePath).Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    $current = Split-Path $collector -Parent
    while ($current.StartsWith(
            $PitCrewRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        Add-WindowsExpectedEvidenceAce `
            -Expected $expected `
            -Path $current `
            -Rights $traverseRights
        if ($current.Equals(
                $PitCrewRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = Split-Path $current -Parent
    }
    Add-WindowsExpectedEvidenceAce `
        -Expected $expected `
        -Path $collector `
        -Rights ([Security.AccessControl.FileSystemRights]::Read)
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        Add-WindowsExpectedEvidenceAce `
            -Expected $expected `
            -Path $connectorRoot `
            -Rights $traverseRights
    }
    if (Test-Path `
            -LiteralPath $Paths.ConnectorHealthRoot `
            -PathType Container) {
        Add-WindowsExpectedEvidenceAce `
            -Expected $expected `
            -Path $Paths.ConnectorHealthRoot `
            -Rights $traverseRights
        foreach ($fileName in $policy.connectorHealthFiles) {
            $path = Join-Path $Paths.ConnectorHealthRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Add-WindowsExpectedEvidenceAce `
                    -Expected $expected `
                    -Path $path `
                    -Rights (
                        [Security.AccessControl.FileSystemRights]::Read
                    )
            }
        }
    }
    return ,$expected
}

function Assert-WindowsEvidenceAclsExact {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Settings
    )

    $pitCrewRoot = [string]$Settings.PitCrewRoot
    $profiles = ([string]$Settings.AllowedProfiles).Split(',')
    $brokerSid = [string]$Settings.BrokerServiceSid
    $agentSid = [string]$Settings.ExpectedAgentSid
    $expected = Get-WindowsExpectedEvidenceAces `
        -Paths $Paths `
        -PitCrewRoot $pitCrewRoot `
        -Profiles $profiles
    $scanRoots = [Collections.Generic.List[string]]::new()
    $scanRoots.Add($pitCrewRoot)
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        $scanRoots.Add($connectorRoot)
    }
    foreach ($scanRoot in $scanRoots) {
        $items = @(
            Get-Item -LiteralPath $scanRoot -Force
            Get-ChildItem `
                -LiteralPath $scanRoot `
                -Recurse `
                -Force `
                -ErrorAction Stop
        )
        foreach ($item in $items) {
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            $rules = @(
                (Get-Acl -LiteralPath $fullPath).GetAccessRules(
                    $true,
                    $true,
                    [Security.Principal.SecurityIdentifier])
            )
            $brokerRules = @(
                $rules |
                    Where-Object {
                        $_.IdentityReference.Value -eq $brokerSid
                    }
            )
            if (-not $expected.ContainsKey($fullPath)) {
                if ($brokerRules.Count -gt 0) {
                    throw 'The broker has an unexpected evidence ACE.'
                }
            } elseif ($brokerRules.Count -ne 1) {
                throw 'The broker evidence ACE count is not exact.'
            } else {
                $rule = $brokerRules[0]
                $actualRights =
                    Get-WindowsNormalizedRights -Rights $rule.FileSystemRights
                if ($rule.AccessControlType -ne
                        [Security.AccessControl.AccessControlType]::Allow -or
                    $rule.IsInherited -or
                    $rule.InheritanceFlags -ne
                        [Security.AccessControl.InheritanceFlags]::None -or
                    $rule.PropagationFlags -ne
                        [Security.AccessControl.PropagationFlags]::None -or
                    $actualRights -ne $expected[$fullPath]) {
                    throw 'The broker evidence ACE rights or inheritance are not exact.'
                }
            }
            $agentRules = @(
                $rules |
                    Where-Object {
                        $_.IdentityReference.Value -eq $agentSid
                    }
            )
            if ($agentRules.Count -ne 1) {
                throw 'The support agent evidence denial count is not exact.'
            }
            $agentRule = $agentRules[0]
            $agentRights = Get-WindowsNormalizedRights `
                -Rights $agentRule.FileSystemRights
            $fullControl = Get-WindowsNormalizedRights `
                -Rights (
                    [Security.AccessControl.FileSystemRights]::FullControl
                )
            $isEvidenceRoot =
                $fullPath.Equals(
                    $pitCrewRoot,
                    [StringComparison]::OrdinalIgnoreCase) -or
                $fullPath.Equals(
                    $connectorRoot,
                    [StringComparison]::OrdinalIgnoreCase)
            $expectedInheritance = if ($item.PSIsContainer) {
                [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                    [Security.AccessControl.InheritanceFlags]::ObjectInherit
            } else {
                [Security.AccessControl.InheritanceFlags]::None
            }
            if ($agentRule.AccessControlType -ne
                    [Security.AccessControl.AccessControlType]::Deny -or
                $agentRights -ne $fullControl -or
                $agentRule.IsInherited -eq $isEvidenceRoot -or
                $agentRule.InheritanceFlags -ne $expectedInheritance -or
                $agentRule.PropagationFlags -ne
                    [Security.AccessControl.PropagationFlags]::None) {
                throw 'The support agent evidence denial is not exact.'
            }
        }
    }
    foreach ($root in @($pitCrewRoot, $connectorRoot)) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }
        $rootAgentRules = @(
            (Get-Acl -LiteralPath $root).GetAccessRules(
                $true,
                $false,
                [Security.Principal.SecurityIdentifier]) |
                Where-Object {
                    $_.IdentityReference.Value -eq $agentSid
                }
        )
        if ($rootAgentRules.Count -ne 1 -or
            $rootAgentRules[0].AccessControlType -ne
                [Security.AccessControl.AccessControlType]::Deny -or
            $rootAgentRules[0].InheritanceFlags -ne (
                [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [Security.AccessControl.InheritanceFlags]::ObjectInherit
            )) {
            throw 'The support agent root evidence denial is not exact.'
        }
    }
}

function Get-LinuxAclEntries {
    param([Parameter(Mandatory)][string]$Path)

    $output = & getfacl `
        '--absolute-names' `
        '--numeric' `
        '--omit-header' `
        '--' `
        $Path
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect an evidence ACL.'
    }
    $entries = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    foreach ($line in $output) {
        $value = ([string]$line).Trim()
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }
        $parts = $value.Split(':')
        $offset = if ($parts[0] -eq 'default') { 1 } else { 0 }
        $prefix = if ($offset -eq 1) { 'default:' } else { '' }
        if ($parts.Count -lt ($offset + 3)) {
            throw 'An evidence ACL entry could not be parsed.'
        }
        $permissions = $parts[$offset + 2].Trim()
        if ($permissions.Length -lt 3) {
            throw 'An evidence ACL permission value could not be parsed.'
        }
        $entries["$prefix$($parts[$offset]):$($parts[$offset + 1])"] =
            $permissions.Substring(0, 3)
    }
    return ,$entries
}

function Get-LinuxEffectivePermissions {
    param(
        [Parameter(Mandatory)][string]$Permissions,
        [Parameter(Mandatory)][string]$Mask
    )

    $effective = [char[]]'---'
    for ($index = 0; $index -lt 3; $index++) {
        if ($Permissions[$index] -ne '-' -and $Mask[$index] -ne '-') {
            $effective[$index] = $Permissions[$index]
        }
    }
    return -join $effective
}

function Add-LinuxExpectedEvidenceAccess {
    param(
        [Parameter(Mandatory)]
        [Collections.Generic.Dictionary[string, string]]$Expected,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Permissions
    )

    $Expected[[IO.Path]::GetFullPath($Path)] = $Permissions
}

function Get-LinuxExpectedEvidenceAccess {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$PitCrewRoot,
        [Parameter(Mandatory)][string[]]$Profiles
    )

    $expected = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    $policy = Get-EvidencePolicy
    Add-LinuxExpectedEvidenceAccess `
        -Expected $expected `
        -Path $PitCrewRoot `
        -Permissions '--x'
    foreach ($sentinel in $policy.installationSentinels) {
        Add-LinuxExpectedEvidenceAccess `
            -Expected $expected `
            -Path (Join-Path $PitCrewRoot $sentinel) `
            -Permissions '---'
    }
    $stateRoot = Join-Path $PitCrewRoot '.pitcrew-state'
    Add-LinuxExpectedEvidenceAccess `
        -Expected $expected `
        -Path $stateRoot `
        -Permissions 'r-x'
    foreach ($profile in $Profiles) {
        $profileRoot = Join-Path $stateRoot $profile
        Add-LinuxExpectedEvidenceAccess `
            -Expected $expected `
            -Path $profileRoot `
            -Permissions '--x'
        foreach ($fileName in $policy.profileProjectionFiles) {
            $path = Join-Path $profileRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Add-LinuxExpectedEvidenceAccess `
                    -Expected $expected `
                    -Path $path `
                    -Permissions 'r--'
            }
        }
    }
    $collector = Join-Path (
        $PitCrewRoot
    ) ([string]$policy.collectorRelativePath).Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    $current = Split-Path $collector -Parent
    while ($current.StartsWith(
            $PitCrewRoot,
            [StringComparison]::Ordinal)) {
        Add-LinuxExpectedEvidenceAccess `
            -Expected $expected `
            -Path $current `
            -Permissions '--x'
        if ($current -ceq $PitCrewRoot) {
            break
        }
        $current = Split-Path $current -Parent
    }
    Add-LinuxExpectedEvidenceAccess `
        -Expected $expected `
        -Path $collector `
        -Permissions 'r--'
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        Add-LinuxExpectedEvidenceAccess `
            -Expected $expected `
            -Path $connectorRoot `
            -Permissions '--x'
    }
    if (Test-Path `
            -LiteralPath $Paths.ConnectorHealthRoot `
            -PathType Container) {
        Add-LinuxExpectedEvidenceAccess `
            -Expected $expected `
            -Path $Paths.ConnectorHealthRoot `
            -Permissions '--x'
        foreach ($fileName in $policy.connectorHealthFiles) {
            $path = Join-Path $Paths.ConnectorHealthRoot $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Add-LinuxExpectedEvidenceAccess `
                    -Expected $expected `
                    -Path $path `
                    -Permissions 'r--'
            }
        }
    }
    return ,$expected
}

function Get-LinuxEvidenceMetadataFingerprint {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$PitCrewRoot,
        [Parameter(Mandatory)][string[]]$Profiles
    )

    $expected = Get-LinuxExpectedEvidenceAccess `
        -Paths $Paths `
        -PitCrewRoot $PitCrewRoot `
        -Profiles $Profiles
    $policy = Get-EvidencePolicy
    $replaceableFiles =
        [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
    foreach ($profile in $Profiles) {
        foreach ($fileName in $policy.profileProjectionFiles) {
            $replaceableFiles.Add(
                [IO.Path]::GetFullPath(
                    (Join-Path (
                        Join-Path (
                            Join-Path $PitCrewRoot '.pitcrew-state'
                        ) $profile
                    ) $fileName))) |
                Out-Null
        }
    }
    foreach ($fileName in $policy.connectorHealthFiles) {
        $replaceableFiles.Add(
            [IO.Path]::GetFullPath(
                (Join-Path $Paths.ConnectorHealthRoot $fileName))) |
            Out-Null
    }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($path in @($expected.Keys | Sort-Object)) {
        if ($replaceableFiles.Contains($path)) {
            continue
        }
        $metadata = (& stat '--format=%u:%g:%a:%f' -- $path).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not inspect evidence ownership and mode.'
        }
        $parts = $metadata.Split(':')
        if ($parts.Count -ne 4) {
            throw 'Evidence ownership and mode metadata is invalid.'
        }
        $mode = [Convert]::ToInt32($parts[2], 8)
        $stableMode = $mode -band [Convert]::ToInt32('7707', 8)
        $fileType = [Convert]::ToInt32($parts[3], 16) -band 0xf000
        $lines.Add(
            "$path`n$($parts[0]):$($parts[1]):$stableMode`:$fileType")
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(
        ($lines -join "`n"))
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)
    ).ToLowerInvariant()
}

function Get-LinuxEvidenceMetadataPath {
    param([Parameter(Mandatory)][hashtable]$Paths)

    return Join-Path `
        $Paths.InstallerStateRoot `
        'evidence-metadata-v0.10.0.json'
}

function Assert-LinuxEvidenceMetadataExact {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$PitCrewRoot,
        [Parameter(Mandatory)][string[]]$Profiles
    )

    $snapshotPath = Get-LinuxEvidenceMetadataPath -Paths $Paths
    if (-not (Test-Path -LiteralPath $snapshotPath -PathType Leaf)) {
        throw 'The installed evidence ownership and mode contract is missing.'
    }
    $snapshot = Get-Content `
        -LiteralPath $snapshotPath `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json
    $actual = Get-LinuxEvidenceMetadataFingerprint `
        -Paths $Paths `
        -PitCrewRoot $PitCrewRoot `
        -Profiles $Profiles
    if ($snapshot.schemaVersion -ne 1 -or
        $snapshot.sha256 -cne $actual) {
        throw 'Evidence ownership or mode drift was detected.'
    }
}

function Initialize-LinuxEvidenceMetadataContract {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$PitCrewRoot,
        [Parameter(Mandatory)][string[]]$Profiles
    )

    $snapshotPath = Get-LinuxEvidenceMetadataPath -Paths $Paths
    if (Test-Path -LiteralPath $snapshotPath -PathType Leaf) {
        Assert-LinuxEvidenceMetadataExact `
            -Paths $Paths `
            -PitCrewRoot $PitCrewRoot `
            -Profiles $Profiles
        return
    }
    $snapshot = [ordered]@{
        schemaVersion = 1
        sha256 = Get-LinuxEvidenceMetadataFingerprint `
            -Paths $Paths `
            -PitCrewRoot $PitCrewRoot `
            -Profiles $Profiles
    }
    [IO.File]::WriteAllText(
        $snapshotPath,
        ($snapshot | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
    Invoke-Checked chmod @('600', $snapshotPath)
}

function Assert-LinuxEvidenceAclsExact {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Settings
    )

    $pitCrewRoot = [string]$Settings.PitCrewRoot
    $profiles = ([string]$Settings.AllowedProfiles).Split(',')
    $agentUid = [string]$Settings.ExpectedAgentUid
    $brokerUid = [string]$Settings.BrokerUid
    $ipcGroupGid = [string]$Settings.IpcGroupGid
    $productGroupIds = @(
        $linuxAgentUser,
        $linuxBrokerUser,
        $linuxIpcGroup
    ) | ForEach-Object {
        $entry = & getent group $_
        if ($LASTEXITCODE -ne 0) {
            throw 'A support-plane service group is unavailable.'
        }
        $fields = ([string]$entry).Split(':')
        if ($fields.Count -lt 3) {
            throw 'A support-plane service group is invalid.'
        }
        $fields[2]
    } | Sort-Object -Unique
    if ($ipcGroupGid -notin $productGroupIds) {
        throw 'The configured support IPC group is invalid.'
    }
    $expected = Get-LinuxExpectedEvidenceAccess `
        -Paths $Paths `
        -PitCrewRoot $pitCrewRoot `
        -Profiles $profiles
    Assert-LinuxEvidenceMetadataExact `
        -Paths $Paths `
        -PitCrewRoot $pitCrewRoot `
        -Profiles $profiles
    $scanRoots = [Collections.Generic.List[string]]::new()
    $scanRoots.Add($pitCrewRoot)
    $connectorRoot = Split-Path $Paths.ConnectorHealthRoot -Parent
    if (Test-Path -LiteralPath $connectorRoot -PathType Container) {
        $scanRoots.Add($connectorRoot)
    }
    foreach ($scanRoot in $scanRoots) {
        $items = @(
            Get-Item -LiteralPath $scanRoot -Force
            Get-ChildItem `
                -LiteralPath $scanRoot `
                -Recurse `
                -Force `
                -ErrorAction Stop
        )
        foreach ($item in $items) {
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            $entries = Get-LinuxAclEntries -Path $fullPath
            $brokerKey = "user:$brokerUid"
            $agentKey = "user:$agentUid"
            $expectedBroker = if ($expected.ContainsKey($fullPath)) {
                $expected[$fullPath]
            } else {
                '---'
            }
            if (-not $entries.ContainsKey($brokerKey) -or
                $entries[$brokerKey] -cne $expectedBroker -or
                -not $entries.ContainsKey($agentKey) -or
                $entries[$agentKey] -cne '---' -or
                -not $entries.ContainsKey('mask:')) {
                throw 'A named Linux evidence ACL is not exact.'
            }
            $brokerEffective = Get-LinuxEffectivePermissions `
                -Permissions $entries[$brokerKey] `
                -Mask $entries['mask:']
            $agentEffective = Get-LinuxEffectivePermissions `
                -Permissions $entries[$agentKey] `
                -Mask $entries['mask:']
            if ($brokerEffective -cne $expectedBroker -or
                $agentEffective -cne '---') {
                throw 'A Linux evidence ACL mask changes effective product access.'
            }
            foreach ($productGroupId in $productGroupIds) {
                if ($entries.ContainsKey("group:$productGroupId") -or
                    $entries.ContainsKey(
                        "default:group:$productGroupId")) {
                    throw 'A product service group has an unexpected evidence ACL.'
                }
            }
            if ($item.PSIsContainer) {
                $defaultBrokerKey = "default:user:$brokerUid"
                $defaultAgentKey = "default:user:$agentUid"
                if (-not $entries.ContainsKey($defaultBrokerKey) -or
                    $entries[$defaultBrokerKey] -cne '---' -or
                    -not $entries.ContainsKey($defaultAgentKey) -or
                    $entries[$defaultAgentKey] -cne '---' -or
                    -not $entries.ContainsKey('default:mask:') -or
                    (Get-LinuxEffectivePermissions `
                        -Permissions $entries[$defaultBrokerKey] `
                        -Mask $entries['default:mask:']) -cne '---' -or
                    (Get-LinuxEffectivePermissions `
                        -Permissions $entries[$defaultAgentKey] `
                        -Mask $entries['default:mask:']) -cne '---') {
                    throw 'A default Linux evidence ACL is not exact.'
                }
            }
            $metadata = (& stat '--format=%u:%g:%a' -- $fullPath).Trim()
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not inspect evidence ownership and mode.'
            }
            $metadataParts = $metadata.Split(':')
            $mode = if ($metadataParts.Count -eq 3) {
                [Convert]::ToInt32($metadataParts[2], 8)
            } else {
                -1
            }
            if ($metadataParts.Count -ne 3 -or
                $metadataParts[0] -in @($agentUid, $brokerUid) -or
                $metadataParts[1] -in $productGroupIds -or
                ($expected.ContainsKey($fullPath) -and (
                    ($mode -band [Convert]::ToInt32('7000', 8)) -ne 0 -or
                    ($mode -band [Convert]::ToInt32('0022', 8)) -ne 0
                ))) {
                throw 'Evidence ownership or mode grants an unexpected product identity or special permission.'
            }
        }
    }
}

function Assert-EvidenceFilesReadable {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Settings
    )

    $policy = Get-EvidencePolicy
    $pitCrewRoot = [string]$Settings.PitCrewRoot
    $profiles = ([string]$Settings.AllowedProfiles).Split(',')
    $stateRoot = Join-Path $pitCrewRoot '.pitcrew-state'
    $collector = Join-Path (
        $pitCrewRoot
    ) ([string]$policy.collectorRelativePath).Replace(
        '/',
        [IO.Path]::DirectorySeparatorChar)
    $files = [Collections.Generic.List[string]]::new()
    $files.Add($collector)
    $collectorHash = (
        Get-FileHash -LiteralPath $collector -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($collectorHash -cne [string]$policy.collectorSha256) {
        throw 'The fixed PitCrew v0.10.0 diagnostics collector hash is invalid.'
    }
    foreach ($profile in $profiles) {
        foreach ($fileName in $policy.profileProjectionFiles) {
            $path = Join-Path (
                Join-Path (
                    Join-Path $pitCrewRoot '.pitcrew-state'
                ) $profile
            ) $fileName
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $files.Add($path)
            }
        }
    }
    foreach ($fileName in $policy.connectorHealthFiles) {
        $path = Join-Path $Paths.ConnectorHealthRoot $fileName
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $files.Add($path)
        }
    }
    if ($IsWindows) {
        Assert-WindowsEvidenceAclsExact `
            -Paths $Paths `
            -Settings $Settings
        $brokerSid = [string]$Settings.BrokerServiceSid
        $agentSid = [string]$Settings.ExpectedAgentSid
        $stateEnumerationRule = @(
            (Get-Acl -LiteralPath $stateRoot).GetAccessRules(
                $true,
                $true,
                [Security.Principal.SecurityIdentifier]) |
                Where-Object {
                    $_.IdentityReference.Value -eq $brokerSid -and
                    $_.AccessControlType -eq
                        [Security.AccessControl.AccessControlType]::Allow -and
                    ($_.FileSystemRights -band
                        [Security.AccessControl.FileSystemRights]::ReadData) -ne 0 -and
                    $_.InheritanceFlags -eq
                        [Security.AccessControl.InheritanceFlags]::None
                }
        )
        if ($stateEnumerationRule.Count -eq 0) {
            throw 'The broker cannot enumerate the PitCrew profile-state root.'
        }
        foreach ($sentinel in $policy.installationSentinels) {
            $sentinelRules = @(
                (Get-Acl `
                    -LiteralPath (
                        Join-Path $pitCrewRoot $sentinel
                    )).GetAccessRules(
                    $true,
                    $true,
                    [Security.Principal.SecurityIdentifier]) |
                    Where-Object {
                        $_.IdentityReference.Value -eq $brokerSid -and
                        $_.AccessControlType -eq
                            [Security.AccessControl.AccessControlType]::Allow -and
                        ($_.FileSystemRights -band
                            [Security.AccessControl.FileSystemRights]::ReadAttributes) -ne 0 -and
                        ($_.FileSystemRights -band
                            [Security.AccessControl.FileSystemRights]::ReadData) -eq 0
                    }
            )
            if ($sentinelRules.Count -eq 0) {
                throw 'The PitCrew root-validation metadata ACL is not exact.'
            }
        }
        foreach ($path in $files) {
            $acl = Get-Acl -LiteralPath $path
            $readRule = @(
                $acl.GetAccessRules(
                    $true,
                    $true,
                    [Security.Principal.SecurityIdentifier]) |
                    Where-Object {
                        $_.IdentityReference.Value -eq $brokerSid -and
                        $_.AccessControlType -eq
                            [Security.AccessControl.AccessControlType]::Allow -and
                        ($_.FileSystemRights -band
                            [Security.AccessControl.FileSystemRights]::ReadData) -ne 0
                    }
            )
            if ($readRule.Count -eq 0) {
                throw 'Support evidence ACL drift was detected; reapply the exact file ACLs after atomic replacement.'
            }
        }
        $agentDenied = @(
            (Get-Acl -LiteralPath $files[0]).GetAccessRules(
                $true,
                $true,
                [Security.Principal.SecurityIdentifier]) |
                Where-Object {
                    $_.IdentityReference.Value -eq $agentSid -and
                    $_.AccessControlType -eq
                        [Security.AccessControl.AccessControlType]::Deny -and
                    ($_.FileSystemRights -band
                        [Security.AccessControl.FileSystemRights]::ReadData) -ne 0
                }
        )
        if ($agentDenied.Count -eq 0) {
            throw 'The support transport agent is not denied PitCrew evidence access.'
        }
        $environmentPath = Join-Path $pitCrewRoot '.env'
        if (Test-Path -LiteralPath $environmentPath -PathType Leaf) {
            $environmentAcl = Get-Acl -LiteralPath $environmentPath
            $prohibitedRule = @(
                $environmentAcl.GetAccessRules(
                    $true,
                    $true,
                    [Security.Principal.SecurityIdentifier]) |
                    Where-Object {
                        $_.IdentityReference.Value -eq $brokerSid -and
                        $_.AccessControlType -eq
                            [Security.AccessControl.AccessControlType]::Allow
                    }
            )
            if ($prohibitedRule.Count -gt 0) {
                throw 'The broker can read a prohibited local resource.'
            }
        }
    } else {
        Assert-LinuxEvidenceAclsExact `
            -Paths $Paths `
            -Settings $Settings
        foreach ($access in @('-r', '-x')) {
            & runuser -u $linuxBrokerUser -- test $access $stateRoot
            if ($LASTEXITCODE -ne 0) {
                throw 'The broker cannot enumerate the PitCrew profile-state root.'
            }
        }
        foreach ($sentinel in $policy.installationSentinels) {
            Invoke-Checked runuser @(
                '-u',
                $linuxBrokerUser,
                '--',
                'stat',
                '--format=%F',
                (Join-Path $pitCrewRoot $sentinel)
            )
        }
        foreach ($path in $files) {
            & runuser -u $linuxBrokerUser -- test -r $path
            if ($LASTEXITCODE -ne 0) {
                throw 'Support evidence ACL drift was detected; reapply the exact file ACLs after atomic replacement.'
            }
        }
        foreach ($prohibited in @(
            (Join-Path $pitCrewRoot '.env'),
            '/var/run/docker.sock'
        )) {
            if (Test-Path -LiteralPath $prohibited) {
                & runuser -u $linuxBrokerUser -- test -r $prohibited
                if ($LASTEXITCODE -eq 0) {
                    throw 'The broker can read a prohibited local resource.'
                }
            }
        }
        & runuser -u $linuxAgentUser -- test -r $files[0]
        if ($LASTEXITCODE -eq 0) {
            throw 'The support transport agent can read PitCrew evidence.'
        }
    }
}

function Invoke-Verify {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][object]$Manifest
    )

    $settings = Get-Content `
        -LiteralPath (Join-Path $Paths.BrokerStateRoot 'appsettings.json') `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
    $brokerSettings = $settings.PitCrewSupport.Broker
    Assert-InstalledSupportVersion `
        -Paths $Paths `
        -InstalledVersion ([string]$Manifest.currentVersion)
    if (-not [string]::IsNullOrWhiteSpace(
            [string]$Manifest.previousVersion)) {
        Assert-InstalledSupportVersion `
            -Paths $Paths `
            -InstalledVersion ([string]$Manifest.previousVersion)
    }
    if ($IsWindows) {
        $agent = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='$windowsAgentService'"
        $broker = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='$windowsBrokerService'"
        if ($agent.StartName -eq $broker.StartName -or
            $agent.StartName -ne "NT SERVICE\$windowsAgentService" -or
            $broker.StartName -ne "NT SERVICE\$windowsBrokerService") {
            throw 'The Windows support services do not use separate product identities.'
        }
        $expectedStartMode = if ([bool]$Manifest.enabled) {
            'Auto'
        } else {
            'Disabled'
        }
        $expectedState = if ([bool]$Manifest.enabled) {
            'Running'
        } else {
            'Stopped'
        }
        if ($agent.StartMode -ne $expectedStartMode -or
            $broker.StartMode -ne $expectedStartMode -or
            $agent.State -ne $expectedState -or
            $broker.State -ne $expectedState) {
            throw 'The Windows support service state does not match the lifecycle manifest.'
        }
        $agentSidType = (& sc.exe qsidtype $windowsAgentService | Out-String)
        $brokerSidType = (& sc.exe qsidtype $windowsBrokerService | Out-String)
        if ($agentSidType -notmatch 'UNRESTRICTED' -or
            $brokerSidType -notmatch 'UNRESTRICTED') {
            throw 'The Windows support service SIDs are not enabled.'
        }
        foreach ($serviceContract in @(
            [PSCustomObject]@{
                Name = $windowsAgentService
                Privileges = @('SeChangeNotifyPrivilege')
            },
            [PSCustomObject]@{
                Name = $windowsBrokerService
                Privileges = @(
                    'SeChangeNotifyPrivilege',
                    'SeImpersonatePrivilege'
                )
            }
        )) {
            $serviceName = [string]$serviceContract.Name
            $privilegeOutput = & sc.exe qprivs $serviceName | Out-String
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not inspect the Windows support service privileges.'
            }
            $privileges = @(
                [regex]::Matches(
                    $privilegeOutput,
                    'Se[A-Za-z0-9]+Privilege') |
                    ForEach-Object Value |
                    Sort-Object -Unique
            )
            $expectedPrivileges = @(
                $serviceContract.Privileges |
                    Sort-Object
            )
            if ((@($privileges) -join ',') -cne
                (@($expectedPrivileges) -join ',')) {
                throw 'A Windows support service has unexpected privileges.'
            }
            $serviceStateRoot = if ($serviceName -ceq $windowsAgentService) {
                $Paths.AgentStateRoot
            } else {
                $Paths.BrokerStateRoot
            }
            $environment = @(
                (
                    Get-ItemProperty `
                        -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" `
                        -Name 'Environment' `
                        -ErrorAction Stop
                ).Environment
            )
            $expectedEnvironment =
                "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$(Join-Path $serviceStateRoot 'bundle')"
            if ($environment.Count -ne 1 -or
                $environment[0] -cne $expectedEnvironment) {
                throw 'A Windows support service has an unexpected runtime environment.'
            }
        }
        $rule = Get-NetFirewallRule -Name $windowsFirewallRule
        $serviceFilter = $rule | Get-NetFirewallServiceFilter
        $addressFilter = $rule | Get-NetFirewallAddressFilter
        $portFilter = $rule | Get-NetFirewallPortFilter
        $expectedBrokerExecutable = Join-Path (
            Join-Path (
                Join-Path $Paths.BrokerInstallRoot 'versions'
            ) ([string]$Manifest.currentVersion)
        ) 'PitCrew.Support.Broker.App.exe'
        $expectedAgentExecutable = Join-Path (
            Join-Path (
                Join-Path $Paths.AgentInstallRoot 'versions'
            ) ([string]$Manifest.currentVersion)
        ) 'PitCrew.Support.Agent.App.exe'
        $expectedAgentPath =
            "`"$expectedAgentExecutable`" --contentRoot `"$($Paths.AgentStateRoot)`" --PitCrewSupport:Agent:PipeName=$pipeName --PitCrewSupport:Agent:ReplayRoot=`"$(Join-Path $Paths.AgentStateRoot 'replay')`""
        $expectedBrokerPath =
            "`"$expectedBrokerExecutable`" --contentRoot `"$($Paths.BrokerStateRoot)`""
        $agentDependencies = @(
            (Get-Service -Name $windowsAgentService).RequiredServices |
                ForEach-Object ServiceName
        )
        if ($rule.Enabled -ne 'True' -or
            $rule.Action -ne 'Block' -or
            $rule.Direction -ne 'Outbound' -or
            $rule.Profile -ne 'Any' -or
            (@($addressFilter.LocalAddress) -join ',') -cne 'Any' -or
            (@($addressFilter.RemoteAddress) -join ',') -cne 'Any' -or
            $portFilter.Protocol -ne 'Any' -or
            (@($portFilter.LocalPort) -join ',') -cne 'Any' -or
            (@($portFilter.RemotePort) -join ',') -cne 'Any' -or
            -not $serviceFilter.Service.Equals(
                $windowsBrokerService,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not $agent.PathName.Equals(
                $expectedAgentPath,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not $broker.PathName.Equals(
                $expectedBrokerPath,
                [StringComparison]::OrdinalIgnoreCase) -or
            (@($agentDependencies) -join ',') -cne
                $windowsBrokerService) {
            throw 'The broker outbound firewall or active service binary is not exact.'
        }
        Assert-WindowsBrokerHasNoDockerAccess
    } else {
        Assert-LinuxCurrentVersion `
            -Paths $Paths `
            -InstalledVersion ([string]$Manifest.currentVersion)
        Assert-EffectiveLinuxServiceBoundary -Paths $Paths
        foreach ($service in @($linuxAgentService, $linuxBrokerService)) {
            $unitFileState = [string](
                Get-SystemdProperty `
                    -Unit $service `
                    -Property 'UnitFileState')
            $activeState = [string](
                Get-SystemdProperty `
                    -Unit $service `
                    -Property 'ActiveState')
            $expectedUnitFileState = if ([bool]$Manifest.enabled) {
                'enabled'
            } else {
                'disabled'
            }
            $expectedActiveState = if ([bool]$Manifest.enabled) {
                'active'
            } else {
                'inactive'
            }
            if ($unitFileState -cne $expectedUnitFileState -or
                $activeState -cne $expectedActiveState) {
                throw "The Linux support service state does not match the lifecycle manifest. Bounded diagnostics: UnitFileState=$unitFileState;ActiveState=$activeState"
            }
        }
        $agentGroups = @(
            (& id -nG $linuxAgentUser).Split(' ') |
                Sort-Object
        )
        $brokerGroups = @(
            (& id -nG $linuxBrokerUser).Split(' ') |
                Sort-Object
        )
        $expectedAgentGroups = @(
            @($linuxAgentUser, $linuxIpcGroup) |
                Sort-Object
        )
        $expectedBrokerGroups = @(
            @($linuxBrokerUser, $linuxIpcGroup) |
                Sort-Object
        )
        if ((@($agentGroups) -join ',') -cne
                (@($expectedAgentGroups) -join ',') -or
            (@($brokerGroups) -join ',') -cne
                (@($expectedBrokerGroups) -join ',')) {
            throw 'A Linux support identity has an unexpected supplementary group.'
        }
    }
    Assert-EvidenceFilesReadable `
        -Paths $Paths `
        -Settings $brokerSettings
}

function Invoke-RepairEvidenceAcl {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $settings = Get-Content `
        -LiteralPath (Join-Path $Paths.BrokerStateRoot 'appsettings.json') `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json -Depth 10
    $brokerSettings = $settings.PitCrewSupport.Broker
    $profiles = ([string]$brokerSettings.AllowedProfiles).Split(',')
    if ($IsWindows) {
        Grant-WindowsBrokerEvidence `
            -ResolvedPitCrewRoot ([string]$brokerSettings.PitCrewRoot) `
            -AllowedProfiles $profiles `
            -BrokerSid ([string]$brokerSettings.BrokerServiceSid) `
            -AgentSid ([string]$brokerSettings.ExpectedAgentSid) `
            -Paths $Paths
    } else {
        Assert-LinuxEvidenceMetadataExact `
            -Paths $Paths `
            -PitCrewRoot ([string]$brokerSettings.PitCrewRoot) `
            -Profiles $profiles
        Grant-LinuxBrokerEvidence `
            -ResolvedPitCrewRoot ([string]$brokerSettings.PitCrewRoot) `
            -AllowedProfiles $profiles `
            -Paths $Paths `
            -AgentUid ([uint]$brokerSettings.ExpectedAgentUid) `
            -BrokerUid ([uint]$brokerSettings.BrokerUid)
    }
    Assert-EvidenceFilesReadable `
        -Paths $Paths `
        -Settings $brokerSettings
}

if (-not ($IsWindows -or $IsLinux)) {
    throw 'The support-plane installer supports Windows and Linux only.'
}

$mutatingActions = @(
    'Install',
    'Update',
    'Enable',
    'Disable',
    'Uninstall',
    'Rollback',
    'RepairEvidenceAcl'
)
if ($Action -in $mutatingActions) {
    Assert-MutatingActionAllowed
}
Assert-PlatformAdministrator

$requiredCommands = [Collections.Generic.List[string]]::new()
$requiredCommands.Add('tar')
if ($IsWindows) {
    $requiredCommands.Add('icacls.exe')
    $requiredCommands.Add('sc.exe')
    foreach ($cmdlet in @(
        'Get-LocalGroup',
        'Get-LocalGroupMember',
        'Get-NetFirewallRule',
        'Get-NetFirewallAddressFilter',
        'Get-NetFirewallPortFilter',
        'Get-NetFirewallServiceFilter',
        'New-NetFirewallRule',
        'Remove-NetFirewallRule'
    )) {
        if ($null -eq (Get-Command $cmdlet -ErrorAction SilentlyContinue)) {
            throw "Required Windows command '$cmdlet' is unavailable."
        }
    }
} else {
    foreach ($command in @(
        'chown',
        'chmod',
        'getfacl',
        'getent',
        'groupdel',
        'groupadd',
        'id',
        'ln',
        'mv',
        'runuser',
        'setfacl',
        'stat',
        'systemctl',
        'userdel',
        'useradd',
        'usermod'
    )) {
        $requiredCommands.Add($command)
    }
}
foreach ($command in $requiredCommands) {
    if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' is unavailable."
    }
}

$paths = Get-PlatformPaths
$installerLock = Enter-InstallerLock -Paths $paths
try {
    $manifest = Get-InstallManifest -Paths $paths
    switch ($Action) {
        'Install' {
            Assert-NoAmbiguousInstallation -Paths $paths -Manifest $manifest
            if ($null -ne $manifest) {
                throw 'Use Update for an existing managed support installation.'
            }
            Invoke-InstallOrUpdate `
                -Paths $paths `
                -Manifest $null `
                -IsUpdate $false
        }
        'Update' {
            if ($null -eq $manifest) {
                throw 'Update requires an existing managed support installation.'
            }
            Invoke-InstallOrUpdate `
                -Paths $paths `
                -Manifest $manifest `
                -IsUpdate $true
        }
        'Enable' {
            if ($null -eq $manifest) {
                throw 'Enable requires an existing managed support installation.'
            }
            Invoke-Enable -Paths $paths -Manifest $manifest
        }
        'Disable' {
            if ($null -eq $manifest) {
                throw 'Disable requires an existing managed support installation.'
            }
            Invoke-Disable -Paths $paths -Manifest $manifest
        }
        'Rollback' {
            if ($null -eq $manifest) {
                throw 'Rollback requires an existing managed support installation.'
            }
            Invoke-Rollback -Paths $paths -Manifest $manifest
        }
        'Uninstall' {
            if ($null -eq $manifest) {
                throw 'Uninstall requires an existing managed support installation.'
            }
            Invoke-Uninstall -Paths $paths
        }
        'RepairEvidenceAcl' {
            if ($null -eq $manifest) {
                throw 'ACL repair requires an existing managed support installation.'
            }
            Invoke-RepairEvidenceAcl -Paths $paths
        }
        'Verify' {
            if ($null -eq $manifest) {
                throw 'Verify requires an existing managed support installation.'
            }
            Invoke-Verify -Paths $paths -Manifest $manifest
        }
    }
} finally {
    $installerLock.Dispose()
}
