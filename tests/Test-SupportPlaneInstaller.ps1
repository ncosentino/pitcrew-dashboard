#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AgentPublishRoot,

    [Parameter(Mandatory)]
    [string]$BrokerPublishRoot,

    [string]$NetworkProbePublishRoot = '',

    [switch]$AllowMachineChanges
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $AllowMachineChanges) {
    throw 'Pass -AllowMachineChanges to run support-plane host lifecycle tests.'
}
if (-not ($IsWindows -or $IsLinux)) {
    throw 'Support-plane host lifecycle tests support Windows and Linux only.'
}
if ($IsWindows) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole(
                [Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Windows support-plane host tests require elevation.'
        }
    } finally {
        $identity.Dispose()
    }
} elseif ([int](& id -u) -ne 0) {
    throw 'Linux support-plane host tests require root.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installerPath = Join-Path (
    $repositoryRoot
) 'scripts' 'Install-PitCrewSupportPlane.ps1'
$testRoot = Join-Path (
    $PSScriptRoot
) ".support-installer-host-$([Guid]::NewGuid().ToString('N'))"
$errors = [Collections.Generic.List[string]]::new()
$checks = 0
$installed = $false
$createdConnectorFixture = $false
$linuxSupplementaryTestUser = 'pitcrew-support-test-member'
$linuxPrimaryGroupTestUser = 'pitcrew-support-test-primary'
$linuxExternalTestGroup = 'pitcrew-support-test-external'

function Add-Check {
    param(
        [object]$Condition,
        [Parameter(Mandatory)][string]$Failure
    )

    $script:checks++
    if (-not $Condition) {
        $script:errors.Add($Failure)
    }
}

function Get-InstalledPaths {
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
    }
}

function New-TestArchive {
    param(
        [Parameter(Mandatory)][string]$Component,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$PublishRoot,
        [switch]$Broken
    )

    $payload = Join-Path $testRoot "$Component-$Version"
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    $executable = if ($IsWindows) {
        if ($Component -eq 'agent') {
            'PitCrew.Support.Agent.App.exe'
        } else {
            'PitCrew.Support.Broker.App.exe'
        }
    } elseif ($Component -eq 'agent') {
        'PitCrew.Support.Agent.App'
    } else {
        'PitCrew.Support.Broker.App'
    }
    if ($Broken) {
        [IO.File]::WriteAllText(
            (Join-Path $payload $executable),
            'not-an-executable',
            [Text.UTF8Encoding]::new($false))
        if ($IsLinux) {
            & chmod 755 (Join-Path $payload $executable)
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not mark the broken test payload executable.'
            }
        }
    } else {
        Copy-Item `
            -Path (Join-Path (Resolve-Path $PublishRoot).Path '*') `
            -Destination $payload `
            -Recurse `
            -Force
    }
    $archive = Join-Path $testRoot "$Component-$Version.tar.gz"
    & tar -C $payload -czf $archive .
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create a support-plane test archive.'
    }
    $checksum = "$archive.sha256"
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).
        Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $checksum,
        "$hash  $(Split-Path $archive -Leaf)",
        [Text.UTF8Encoding]::new($false))
    return @{
        Archive = $archive
        Checksum = $checksum
    }
}

function New-TestRelease {
    param(
        [Parameter(Mandatory)][string]$Version,
        [switch]$BrokenBroker
    )

    return @{
        Agent = New-TestArchive `
            -Component 'agent' `
            -Version $Version `
            -PublishRoot $AgentPublishRoot
        Broker = New-TestArchive `
            -Component 'broker' `
            -Version $Version `
            -PublishRoot $BrokerPublishRoot `
            -Broken:$BrokenBroker
    }
}

function Invoke-Installer {
    param(
        [Parameter(Mandatory)][string]$LifecycleAction,
        [AllowEmptyString()][string]$ReleaseVersion = '',
        [AllowNull()][hashtable]$Release = $null,
        [AllowEmptyString()][string]$PitCrewRoot = '',
        [AllowEmptyString()][string]$SettingsPath = '',
        [string]$Identity = 'PreserveKeys'
    )

    $parameters = @{
        Action = $LifecycleAction
        AllowMachineChanges = $true
        IdentityHandling = $Identity
    }
    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        $parameters.Version = $ReleaseVersion
    }
    if (-not [string]::IsNullOrWhiteSpace($PitCrewRoot)) {
        $parameters.PitCrewRoot = $PitCrewRoot
        $parameters.Profiles = @('default')
    }
    if (-not [string]::IsNullOrWhiteSpace($SettingsPath)) {
        $parameters.AgentSettingsPath = $SettingsPath
    }
    if ($null -ne $Release) {
        $parameters.AgentArchivePath = $Release.Agent.Archive
        $parameters.AgentChecksumPath = $Release.Agent.Checksum
        $parameters.BrokerArchivePath = $Release.Broker.Archive
        $parameters.BrokerChecksumPath = $Release.Broker.Checksum
    }
    & $installerPath @parameters
}

function Get-Manifest {
    param([Parameter(Mandatory)][hashtable]$Paths)

    return Get-Content `
        -LiteralPath (Join-Path $Paths.InstallerStateRoot 'install-state.json') `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json
}

function Test-PublicTcpConnection {
    param(
        [Parameter(Mandatory)][Net.IPAddress]$Address,
        [Parameter(Mandatory)][int]$Port
    )

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($Address, $Port)
        if (-not $task.Wait([TimeSpan]::FromSeconds(10))) {
            return $false
        }
        return $client.Connected
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

function Get-PublicTcpControlEndpoint {
    $port = 443
    $addresses = @(
        [Net.Dns]::GetHostAddresses('example.com') |
            Where-Object AddressFamily -eq InterNetwork |
            Sort-Object -Property IPAddressToString -Unique |
            Select-Object -First 4
    )
    foreach ($address in $addresses) {
        if (Test-PublicTcpConnection -Address $address -Port $port) {
            return @{
                Address = $address.ToString()
                Port = $port
            }
        }
    }
    throw 'The hosted runner could not establish the public network control connection.'
}

function Write-LinuxNetworkProbeScript {
    param([Parameter(Mandatory)][string]$Path)

    [IO.File]::WriteAllText(
        $Path,
        @'
import socket
import sys

result = "denied"
try:
    with socket.create_connection((sys.argv[2], int(sys.argv[3])), timeout=8):
        result = "connected"
except OSError:
    pass

with open(sys.argv[1], "w", encoding="ascii") as output:
    output.write(result)
'@,
        [Text.UTF8Encoding]::new($false))
}

function Invoke-InstalledBrokerNetworkDenialProbe {
    param([Parameter(Mandatory)][hashtable]$Paths)

    $controlEndpoint = Get-PublicTcpControlEndpoint
    $probeFileName = 'network-denial-probe.py'
    $probePath = Join-Path $Paths.BrokerStateRoot $probeFileName
    $resultPath = Join-Path $Paths.BrokerStateRoot 'network-denial-result.txt'
    if ($IsWindows) {
        if ([string]::IsNullOrWhiteSpace($NetworkProbePublishRoot)) {
            throw 'The Windows network probe publish root is required.'
        }
    } else {
        Write-LinuxNetworkProbeScript -Path $probePath
        & chown pitcrew-support-broker:pitcrew-support-broker $probePath
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not assign the Linux network probe to the broker identity.'
        }
        & chmod 500 $probePath
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not protect the Linux network probe.'
        }
    }
    Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    try {
      if ($IsWindows) {
        $service = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='PitCrewSupportBroker'"
        $originalPath = [string]$service.PathName
        Stop-Service -Name PitCrewSupportAgent -Force
        Stop-Service -Name PitCrewSupportBroker -Force
        $brokerExecutable = $originalPath.Split('"')[1]
        $backupExecutable = "$brokerExecutable.real"
        $probeExecutable = Join-Path `
            $NetworkProbePublishRoot `
            'PitCrew.Support.NetworkProbe.App.exe'
        try {
            Move-Item `
                -LiteralPath $brokerExecutable `
                -Destination $backupExecutable `
                -Force
            Copy-Item `
                -LiteralPath $probeExecutable `
                -Destination $brokerExecutable `
                -Force
            $probeCommand = (
                "`"$brokerExecutable`" --result `"$resultPath`" " +
                "--address `"$($controlEndpoint.Address)`" " +
                "--port $($controlEndpoint.Port)"
            )
            & sc.exe config PitCrewSupportBroker 'binPath=' $probeCommand |
                Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not configure the Windows broker network probe.'
            }
            & sc.exe start PitCrewSupportBroker | Out-Null
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            while (-not (Test-Path -LiteralPath $resultPath) -and
                [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 250
            }
        } finally {
            Stop-Service `
                -Name PitCrewSupportBroker `
                -Force `
                -ErrorAction SilentlyContinue
            & sc.exe config PitCrewSupportBroker 'binPath=' $originalPath |
                Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not restore the Windows broker command after the network probe.'
            }
            Remove-Item -LiteralPath $brokerExecutable -Force
            Move-Item `
                -LiteralPath $backupExecutable `
                -Destination $brokerExecutable `
                -Force
            Start-Service -Name PitCrewSupportBroker
            Start-Service -Name PitCrewSupportAgent
        }
      } else {
        $dropInRoot =
            '/etc/systemd/system/pitcrew-support-broker.service.d'
        $dropInPath = Join-Path $dropInRoot '90-network-denial-probe.conf'
        & systemctl stop `
            pitcrew-support-agent.service `
            pitcrew-support-broker.service |
            Out-Null
        try {
            New-Item `
                -ItemType Directory `
                -Path $dropInRoot `
                -Force |
                Out-Null
            [IO.File]::WriteAllText(
                $dropInPath,
                @"
[Service]
ExecStart=
ExecStart=/usr/bin/python3 $probePath $resultPath $($controlEndpoint.Address) $($controlEndpoint.Port)
Restart=no
"@,
                [Text.UTF8Encoding]::new($false))
            & systemctl daemon-reload | Out-Null
            & systemctl start pitcrew-support-broker.service | Out-Null
            if ($LASTEXITCODE -ne 0) {
                $result = (& systemctl show `
                    pitcrew-support-broker.service `
                    --property=ActiveState,SubState,Result,ExecMainStatus `
                    --value) -join ','
                throw "The Linux broker network probe could not start. Bounded diagnostics: $result"
            }
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            while (-not (Test-Path -LiteralPath $resultPath) -and
                [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 250
            }
            if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
                $result = (& systemctl show `
                    pitcrew-support-broker.service `
                    --property=ActiveState,SubState,Result,ExecMainStatus `
                    --value) -join ','
                throw "The Linux broker network probe produced no result. Bounded diagnostics: $result"
            }
        } finally {
            & systemctl stop pitcrew-support-broker.service | Out-Null
            Remove-Item `
                -LiteralPath $dropInPath `
                -Force `
                -ErrorAction SilentlyContinue
            & systemctl daemon-reload | Out-Null
            & systemctl start pitcrew-support-broker.service | Out-Null
            & systemctl start pitcrew-support-agent.service | Out-Null
        }
      }
      $result = if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
          Get-Content -LiteralPath $resultPath -Raw
      } else {
          ''
      }
      return $result.Trim() -eq 'denied'
    } finally {
        Remove-Item `
            -LiteralPath $probePath, $resultPath `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

function Test-NoProductEvidenceAcl {
    param(
        [Parameter(Mandatory)][hashtable]$Paths,
        [Parameter(Mandatory)][string]$PitCrewRoot,
        [Parameter(Mandatory)][object]$BrokerSettings
    )

    $roots = @(
        $PitCrewRoot,
        (Split-Path $Paths.ConnectorHealthRoot -Parent)
    ) | Where-Object {
        Test-Path -LiteralPath $_ -PathType Container
    }
    if ($IsWindows) {
        $sids = @(
            [string]$BrokerSettings.ExpectedAgentSid,
            [string]$BrokerSettings.BrokerServiceSid
        )
        foreach ($root in $roots) {
            foreach ($item in @(
                Get-Item -LiteralPath $root -Force
                Get-ChildItem `
                    -LiteralPath $root `
                    -Recurse `
                    -Force
            )) {
                $rules = @(
                    (Get-Acl -LiteralPath $item.FullName).GetAccessRules(
                        $true,
                        $true,
                        [Security.Principal.SecurityIdentifier]) |
                        Where-Object {
                            $_.IdentityReference.Value -in $sids
                        }
                )
                if ($rules.Count -gt 0) {
                    return $false
                }
            }
        }
        return $true
    }
    $uids = @(
        [string]$BrokerSettings.ExpectedAgentUid,
        [string]$BrokerSettings.BrokerUid
    )
    foreach ($root in $roots) {
        foreach ($item in @(
            Get-Item -LiteralPath $root -Force
            Get-ChildItem `
                -LiteralPath $root `
                -Recurse `
                -Force
        )) {
            $acl = & getfacl `
                '--absolute-names' `
                '--numeric' `
                '--omit-header' `
                '--' `
                $item.FullName
            foreach ($uid in $uids) {
                if (@($acl | Where-Object {
                        $_ -match "^(default:)?user:$uid`:"
                    }).Count -gt 0) {
                    return $false
                }
            }
        }
    }
    return $true
}

function Remove-HostTestResidue {
    $paths = Get-InstalledPaths
    if ($IsWindows) {
        foreach ($name in @('PitCrewSupportAgent', 'PitCrewSupportBroker')) {
            $service = Get-Service -Name $name -ErrorAction SilentlyContinue
            if ($null -ne $service) {
                try {
                    if ($service.Status -ne
                        [ServiceProcess.ServiceControllerStatus]::Stopped) {
                        Stop-Service -Name $name -Force -ErrorAction Stop
                    }
                } finally {
                    $service.Dispose()
                }
                & sc.exe delete $name | Out-Null
            }
        }
        Remove-NetFirewallRule `
            -Name `
                'PitCrewSupportBroker-Outbound-Service-Block',
                'PitCrewSupportBroker-Outbound-Identity-Block',
                'PitCrewSupportBroker-Outbound-Program-Block' `
            -ErrorAction SilentlyContinue
    } else {
        & systemctl disable --now `
            pitcrew-support-agent.service `
            pitcrew-support-broker.service |
            Out-Null
        Remove-Item `
            -LiteralPath `
                '/etc/systemd/system/pitcrew-support-agent.service',
                '/etc/systemd/system/pitcrew-support-broker.service' `
            -Force `
            -ErrorAction SilentlyContinue
        & systemctl daemon-reload | Out-Null
    }
    foreach ($path in @(
        $paths.AgentInstallRoot,
        $paths.BrokerInstallRoot,
        $paths.AgentStateRoot,
        $paths.BrokerStateRoot,
        $paths.InstallerStateRoot,
        $paths.LockPath
    )) {
        Remove-Item `
            -LiteralPath $path `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
    }
    Remove-Item `
        -LiteralPath (Split-Path $paths.LockPath -Parent) `
        -Force `
        -ErrorAction SilentlyContinue
    if ($IsLinux) {
        foreach ($user in @(
            $linuxSupplementaryTestUser,
            $linuxPrimaryGroupTestUser,
            'pitcrew-support-agent',
            'pitcrew-support-broker'
        )) {
            & getent passwd $user | Out-Null
            if ($LASTEXITCODE -eq 0) {
                & userdel $user | Out-Null
            }
        }
        foreach ($group in @(
            $linuxExternalTestGroup,
            'pitcrew-support-agent',
            'pitcrew-support-broker',
            'pitcrew-support-ipc'
        )) {
            & getent group $group | Out-Null
            if ($LASTEXITCODE -eq 0) {
                & groupdel $group | Out-Null
            }
        }
    }
}

$paths = Get-InstalledPaths
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    foreach ($path in @(
        $paths.AgentInstallRoot,
        $paths.BrokerInstallRoot,
        $paths.AgentStateRoot,
        $paths.BrokerStateRoot,
        $paths.InstallerStateRoot
    )) {
        if (Test-Path -LiteralPath $path) {
            throw 'The hosted runner already contains a support-plane installation.'
        }
    }

    $pitCrewRoot = Join-Path $testRoot 'pitcrew'
    $profileRoot = Join-Path $pitCrewRoot '.pitcrew-state' 'default'
    $collectorPath = Join-Path (
        $pitCrewRoot
    ) 'plugins' 'pitcrew-operations' 'skills' `
        'pitcrew-remote-diagnostics' 'scripts' `
        'Collect-PitCrewDiagnostics.ps1'
    New-Item -ItemType Directory -Path $profileRoot -Force | Out-Null
    New-Item `
        -ItemType Directory `
        -Path (Split-Path $collectorPath -Parent) `
        -Force |
        Out-Null
    foreach ($sentinel in @(
        'Setup-Runner.ps1',
        'RunnerProfiles.Functions.ps1',
        'docker-compose.yml'
    )) {
        [IO.File]::WriteAllText(
            (Join-Path $pitCrewRoot $sentinel),
            '',
            [Text.UTF8Encoding]::new($false))
    }
    foreach ($projection in @(
        'desired-capacity.json',
        'acknowledged-capacity.json',
        'static-profile.json',
        'observed-state.json'
    )) {
        [IO.File]::WriteAllText(
            (Join-Path $profileRoot $projection),
            '{}',
            [Text.UTF8Encoding]::new($false))
    }
    [IO.File]::WriteAllText(
        (Join-Path $pitCrewRoot '.env'),
        'PROHIBITED=fixture',
        [Text.UTF8Encoding]::new($false))
    foreach ($healthFile in @(
        'connector-health.json',
        'connector-events.jsonl'
    )) {
        if (Test-Path `
                -LiteralPath (
                    Join-Path $paths.ConnectorHealthRoot $healthFile
                )) {
            throw 'The hosted runner already contains connector-health evidence.'
        }
    }
    New-Item `
        -ItemType Directory `
        -Path $paths.ConnectorHealthRoot `
        -Force |
        Out-Null
    $createdConnectorFixture = $true
    [IO.File]::WriteAllText(
        (Join-Path $paths.ConnectorHealthRoot 'connector-health.json'),
        '{}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $paths.ConnectorHealthRoot 'connector-events.jsonl'),
        '',
        [Text.UTF8Encoding]::new($false))
    Invoke-WebRequest `
        -Uri (
            'https://raw.githubusercontent.com/ncosentino/pitcrew/' +
            '0672c34c/plugins/pitcrew-operations/skills/' +
            'pitcrew-remote-diagnostics/scripts/' +
            'Collect-PitCrewDiagnostics.ps1'
        ) `
        -OutFile $collectorPath
    $collectorHash = (
        Get-FileHash -LiteralPath $collectorPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($collectorHash -cne
        '01e8fbcb54ec7f79d8403284d521c0d98956be2f4a617aa881d490b28f88e0a3') {
        throw 'The hosted collector fixture did not match the pinned policy.'
    }

    $settingsPath = Join-Path $testRoot 'agent-settings.json'
    $identityRoot = Join-Path $paths.AgentStateRoot 'identity'
    $replayRoot = Join-Path $paths.AgentStateRoot 'replay'
    $agentSettings = @{
        PitCrewSupport = @{
            Agent = @{
                IdentityRoot = $identityRoot
                ReplayRoot = $replayRoot
                PipeName = 'pitcrew-support-broker-v1'
                SocketPath = '/run/pitcrew-support/broker.sock'
                DashboardUrl = 'https://127.0.0.1:9/'
                TenantId = 'installer-test'
                NodeId = '11111111-1111-1111-1111-111111111111'
                RelayUrl = 'https://127.0.0.1:9/'
                TransportCredential = 'fixture-transport-credential'
                DashboardAuthorizationSigningPublicKeySpki = 'fixture-auth'
                DashboardResultEncryptionPublicKeySpki = 'fixture-result'
                NodeSigningPrivateKeyPkcs8 = 'fixture-signing'
                NodeEncryptionPrivateKeyPkcs8 = 'fixture-encryption'
                AllowLegacyPrivateKeyConfiguration = $true
            }
        }
    }
    [IO.File]::WriteAllText(
        $settingsPath,
        ($agentSettings | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))

    $releaseOne = New-TestRelease -Version '9.9.90'
    Invoke-Installer `
        -LifecycleAction 'Install' `
        -ReleaseVersion '9.9.90' `
        -Release $releaseOne `
        -PitCrewRoot $pitCrewRoot `
        -SettingsPath $settingsPath
    $installed = $true
    Invoke-Installer -LifecycleAction 'Verify'
    $manifest = Get-Manifest -Paths $paths
    Add-Check (
        $manifest.currentVersion -eq '9.9.90' -and
        [string]::IsNullOrWhiteSpace($manifest.previousVersion)
    ) 'Initial installation did not commit the expected lifecycle state.'

    if ($IsWindows) {
        $agentService = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='PitCrewSupportAgent'"
        $brokerService = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='PitCrewSupportBroker'"
        $agentSid = [Security.Principal.NTAccount]::new(
            'NT SERVICE\PitCrewSupportAgent').Translate(
                [Security.Principal.SecurityIdentifier])
        $brokerSid = [Security.Principal.NTAccount]::new(
            'NT SERVICE\PitCrewSupportBroker').Translate(
                [Security.Principal.SecurityIdentifier])
        Add-Check (
            $agentService.StartName -eq 'NT SERVICE\PitCrewSupportAgent' -and
            $brokerService.StartName -eq 'NT SERVICE\PitCrewSupportBroker' -and
            $agentSid.Value -ne $brokerSid.Value
        ) 'Windows services do not use distinct virtual service identities.'
    } else {
        $brokerUser = (& systemctl show `
            pitcrew-support-broker.service `
            --property=User `
            --value).Trim()
        $privateNetwork = (& systemctl show `
            pitcrew-support-broker.service `
            --property=PrivateNetwork `
            --value).Trim()
        Add-Check (
            $brokerUser -eq 'pitcrew-support-broker' -and
            $privateNetwork -eq 'yes'
        ) 'Linux broker effective identity or network namespace isolation is incorrect.'
    }

    $lockRejected = $false
    $heldLock = [IO.File]::Open(
        $paths.LockPath,
        [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None)
    try {
        try {
            Invoke-Installer -LifecycleAction 'Verify'
        } catch {
            $lockRejected = $_.Exception.Message.Contains(
                'already running',
                [StringComparison]::Ordinal)
        }
    } finally {
        $heldLock.Dispose()
    }
    Add-Check (
        $lockRejected
    ) 'A concurrent privileged lifecycle invocation was not rejected.'

    if ($IsWindows) {
        Disable-NetFirewallRule `
            -Name 'PitCrewSupportBroker-Outbound-Program-Block'
        $disabledFirewallRejected = $false
        try {
            Invoke-Installer -LifecycleAction 'Verify'
        } catch {
            $disabledFirewallRejected = $_.Exception.Message.Contains(
                'firewall',
                [StringComparison]::OrdinalIgnoreCase)
        } finally {
            Enable-NetFirewallRule `
                -Name 'PitCrewSupportBroker-Outbound-Program-Block'
        }
        Add-Check (
            $disabledFirewallRejected
        ) 'A disabled broker outbound firewall rule passed lifecycle verification.'
    } else {
        $overrideRoot =
            '/etc/systemd/system/pitcrew-support-broker.service.d'
        $overridePath = Join-Path $overrideRoot '99-boundary-override.conf'
        New-Item `
            -ItemType Directory `
            -Path $overrideRoot `
            -Force |
            Out-Null
        [IO.File]::WriteAllText(
            $overridePath,
            "[Service]`nBindPaths=/etc`n",
            [Text.UTF8Encoding]::new($false))
        & systemctl daemon-reload | Out-Null
        $dropInRejected = $false
        try {
            Invoke-Installer -LifecycleAction 'Verify'
        } catch {
            $dropInRejected = $_.Exception.Message.Equals(
                'Unexpected effective systemd DropInPaths were detected.',
                [StringComparison]::Ordinal)
        } finally {
            Remove-Item -LiteralPath $overridePath -Force
            & systemctl daemon-reload | Out-Null
        }
        Add-Check (
            $dropInRejected
        ) 'A filesystem-broadening systemd drop-in passed effective-boundary verification.'
    }

    Add-Check (
        Invoke-InstalledBrokerNetworkDenialProbe -Paths $paths
    ) 'The installed broker context established an outbound public TCP connection.'
    Invoke-Installer -LifecycleAction 'Verify'

    Invoke-Installer -LifecycleAction 'Disable'
    $manifest = Get-Manifest -Paths $paths
    Add-Check (
        -not [bool]$manifest.enabled
    ) 'Disable did not persist disabled lifecycle state.'
    Invoke-Installer -LifecycleAction 'Verify'
    Invoke-Installer -LifecycleAction 'Enable'
    $manifest = Get-Manifest -Paths $paths
    Add-Check (
        [bool]$manifest.enabled
    ) 'Enable did not persist enabled lifecycle state.'
    Invoke-Installer -LifecycleAction 'Verify'

    $replacementPath = Join-Path $profileRoot 'observed-state.new'
    [IO.File]::WriteAllText(
        $replacementPath,
        '{}',
        [Text.UTF8Encoding]::new($false))
    Move-Item `
        -LiteralPath $replacementPath `
        -Destination (Join-Path $profileRoot 'observed-state.json') `
        -Force
    $aclDriftDetected = $false
    $aclDriftError = ''
    try {
        Invoke-Installer -LifecycleAction 'Verify'
    } catch {
        $aclDriftError = $_.Exception.Message
        $aclDriftDetected = $_.Exception.Message.Contains(
            'ACL drift',
            [StringComparison]::Ordinal)
    }
    Add-Check (
        $aclDriftDetected
    ) "Atomic projection replacement ACL drift was not reported. Verifier result: $aclDriftError"
    Invoke-Installer -LifecycleAction 'RepairEvidenceAcl'
    Invoke-Installer -LifecycleAction 'Verify'

    $brokerConfiguration = Get-Content `
        -LiteralPath (Join-Path $paths.BrokerStateRoot 'appsettings.json') `
        -Raw |
        ConvertFrom-Json -Depth 10
    $brokerSettings = $brokerConfiguration.PitCrewSupport.Broker
    $projectionPath = Join-Path $profileRoot 'observed-state.json'
    if ($IsWindows) {
        & icacls.exe $projectionPath /grant `
            "*$([string]$brokerSettings.BrokerServiceSid):(F)" |
            Out-Null
    } else {
        & setfacl -m "u:pitcrew-support-broker:rwx" $projectionPath
    }
    $writeDriftRejected = $false
    try {
        Invoke-Installer -LifecycleAction 'Verify'
    } catch {
        $writeDriftRejected = $true
    }
    Add-Check (
        $writeDriftRejected
    ) 'Writable or FullControl broker evidence access passed exact ACL verification.'
    Invoke-Installer -LifecycleAction 'RepairEvidenceAcl'
    Invoke-Installer -LifecycleAction 'Verify'

    if ($IsWindows) {
        & icacls.exe $profileRoot /grant `
            "*$([string]$brokerSettings.BrokerServiceSid):(OI)(CI)F" |
            Out-Null
    } else {
        & setfacl -m "d:u:pitcrew-support-broker:rwx" $profileRoot
    }
    $inheritedDriftRejected = $false
    try {
        Invoke-Installer -LifecycleAction 'Verify'
    } catch {
        $inheritedDriftRejected = $true
    }
    Add-Check (
        $inheritedDriftRejected
    ) 'Inherited/default broker evidence access passed exact ACL verification.'
    Invoke-Installer -LifecycleAction 'RepairEvidenceAcl'
    Invoke-Installer -LifecycleAction 'Verify'

    if ($IsLinux) {
        & chmod o+w $projectionPath
        $modeDriftRejected = $false
        try {
            Invoke-Installer -LifecycleAction 'Verify'
        } catch {
            $modeDriftRejected = $_.Exception.Message.Contains(
                'mode',
                [StringComparison]::Ordinal)
        } finally {
            & chmod o-w $projectionPath
        }
        Add-Check (
            $modeDriftRejected
        ) 'Evidence ownership/mode drift passed lifecycle verification.'
        Invoke-Installer -LifecycleAction 'Verify'
    }

    foreach ($installRoot in @(
        $paths.AgentInstallRoot,
        $paths.BrokerInstallRoot
    )) {
        New-Item `
            -ItemType Directory `
            -Path (Join-Path $installRoot 'versions' '9.9.89') `
            -Force |
            Out-Null
    }
    $releaseTwo = New-TestRelease -Version '9.9.91'
    Invoke-Installer `
        -LifecycleAction 'Update' `
        -ReleaseVersion '9.9.91' `
        -Release $releaseTwo `
        -PitCrewRoot $pitCrewRoot
    Invoke-Installer -LifecycleAction 'Verify'
    $manifest = Get-Manifest -Paths $paths
    Add-Check (
        $manifest.currentVersion -eq '9.9.91' -and
        $manifest.previousVersion -eq '9.9.90'
    ) 'Update did not retain the rollback version.'
    $retainedVersions = @(
        Get-ChildItem `
            -LiteralPath (Join-Path $paths.AgentInstallRoot 'versions') `
            -Directory |
            Select-Object -ExpandProperty Name |
            Sort-Object
    )
    Add-Check (
        (@($retainedVersions) -join ',') -eq '9.9.90,9.9.91'
    ) 'Update retained more than the active and rollback versions.'

    $brokerExecutableName = if ($IsWindows) {
        'PitCrew.Support.Broker.App.exe'
    } else {
        'PitCrew.Support.Broker.App'
    }
    $rollbackBroker = Join-Path (
        Join-Path (
            Join-Path $paths.BrokerInstallRoot 'versions'
        ) '9.9.90'
    ) $brokerExecutableName
    $rollbackBrokerBackup = "$rollbackBroker.rollback-backup"
    Move-Item `
        -LiteralPath $rollbackBroker `
        -Destination $rollbackBrokerBackup
    [IO.File]::WriteAllText(
        $rollbackBroker,
        'not-an-executable',
        [Text.UTF8Encoding]::new($false))
    if ($IsLinux) {
        & chmod 755 $rollbackBroker
    }
    $failedRollbackRestoredCurrent = $false
    try {
        try {
            Invoke-Installer -LifecycleAction 'Rollback'
        } catch {
            $failedManifest = Get-Manifest -Paths $paths
            $failedRollbackRestoredCurrent =
                $failedManifest.currentVersion -eq '9.9.91'
        }
    } finally {
        Remove-Item -LiteralPath $rollbackBroker -Force
        Move-Item `
            -LiteralPath $rollbackBrokerBackup `
            -Destination $rollbackBroker
    }
    Add-Check (
        $failedRollbackRestoredCurrent
    ) 'A failed rollback did not restore the current service version.'
    Invoke-Installer -LifecycleAction 'Verify'

    Invoke-Installer -LifecycleAction 'Rollback'
    $manifest = Get-Manifest -Paths $paths
    Add-Check (
        $manifest.currentVersion -eq '9.9.90' -and
        $manifest.previousVersion -eq '9.9.91'
    ) 'Rollback did not atomically swap current and previous versions.'

    $brokenRelease = New-TestRelease `
        -Version '9.9.92' `
        -BrokenBroker
    $updateFailed = $false
    try {
        Invoke-Installer `
            -LifecycleAction 'Update' `
            -ReleaseVersion '9.9.92' `
            -Release $brokenRelease `
            -PitCrewRoot $pitCrewRoot
    } catch {
        $updateFailed = $true
    }
    $manifest = Get-Manifest -Paths $paths
    Add-Check (
        $updateFailed -and $manifest.currentVersion -eq '9.9.90'
    ) 'A failed update did not restore the prior active version.'
    Invoke-Installer -LifecycleAction 'Verify'

    if ($IsLinux) {
        & groupadd --system $linuxExternalTestGroup
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not create the external group-membership test fixture.'
        }
        try {
            & useradd `
                --system `
                --gid $linuxExternalTestGroup `
                --home-dir /nonexistent `
                --shell /usr/sbin/nologin `
                $linuxSupplementaryTestUser
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not create the supplementary-membership test fixture.'
            }
            try {
                & usermod `
                    -a `
                    -G pitcrew-support-agent `
                    $linuxSupplementaryTestUser
                if ($LASTEXITCODE -ne 0) {
                    throw 'Could not assign the supplementary-membership test fixture.'
                }
                $supplementaryMembershipRejected = $false
                try {
                    Invoke-Installer `
                        -LifecycleAction 'Uninstall' `
                        -Identity 'PreserveKeys'
                } catch {
                    $supplementaryMembershipRejected =
                        $_.Exception.Message.Contains(
                            'supplementary member',
                            [StringComparison]::Ordinal)
                }
                Add-Check (
                    $supplementaryMembershipRejected -and
                    (Test-Path -LiteralPath $paths.AgentInstallRoot)
                ) 'Uninstall mutated state despite external supplementary group membership.'
                Invoke-Installer -LifecycleAction 'Verify'
            } finally {
                & userdel $linuxSupplementaryTestUser | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw 'Could not remove the supplementary-membership test fixture.'
                }
            }

            & useradd `
                --system `
                --gid pitcrew-support-broker `
                --home-dir /nonexistent `
                --shell /usr/sbin/nologin `
                $linuxPrimaryGroupTestUser
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not create the primary-group test fixture.'
            }
            try {
                $primaryGroupUsageRejected = $false
                try {
                    Invoke-Installer `
                        -LifecycleAction 'Uninstall' `
                        -Identity 'PreserveKeys'
                } catch {
                    $primaryGroupUsageRejected =
                        $_.Exception.Message.Contains(
                            'external account primary group',
                            [StringComparison]::Ordinal)
                }
                Add-Check (
                    $primaryGroupUsageRejected -and
                    (Test-Path -LiteralPath $paths.AgentInstallRoot)
                ) 'Uninstall mutated state despite external primary-GID usage.'
                Invoke-Installer -LifecycleAction 'Verify'
            } finally {
                & userdel $linuxPrimaryGroupTestUser | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw 'Could not remove the primary-group test fixture.'
                }
            }
        } finally {
            & groupdel $linuxExternalTestGroup | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not remove the external group-membership test fixture.'
            }
        }
    }

    $brokerConfiguration = Get-Content `
        -LiteralPath (Join-Path $paths.BrokerStateRoot 'appsettings.json') `
        -Raw |
        ConvertFrom-Json -Depth 10
    $uninstallBrokerSettings =
        $brokerConfiguration.PitCrewSupport.Broker
    $identityRoot = Join-Path $paths.AgentStateRoot 'identity'
    $identityExistedBeforeUninstall =
        Test-Path -LiteralPath $identityRoot -PathType Container
    $preservedMarkerPath = if ($IsWindows) {
        Join-Path `
            (Split-Path $paths.AgentStateRoot -Parent) `
            'identity-preserved.json'
    } else {
        Join-Path $paths.AgentStateRoot 'identity-preserved.json'
    }
    $missingIdentityHandlingRejected = $false
    try {
        & $installerPath `
            -Action 'Uninstall' `
            -AllowMachineChanges
    } catch {
        $missingIdentityHandlingRejected = $_.Exception.Message.Contains(
            'Uninstall requires an explicit -IdentityHandling PreserveKeys choice.',
            [StringComparison]::Ordinal)
    }
    Add-Check (
        $missingIdentityHandlingRejected -and
        (Test-Path -LiteralPath $paths.AgentInstallRoot -PathType Container) -and
        (Test-Path -LiteralPath $paths.BrokerInstallRoot -PathType Container)
    ) 'Uninstall without explicit identity handling mutated the installation.'
    Invoke-Installer -LifecycleAction 'Verify'
    Invoke-Installer `
        -LifecycleAction 'Uninstall' `
        -Identity 'PreserveKeys'
    $installed = $false
    Add-Check (
        -not (Test-Path -LiteralPath $paths.AgentInstallRoot) -and
        -not (Test-Path -LiteralPath $paths.BrokerInstallRoot) -and
        (Test-Path `
            -LiteralPath (Join-Path $paths.AgentStateRoot 'appsettings.json') `
            -PathType Leaf)
    ) 'Uninstall did not remove binaries while preserving support identity state.'
    Add-Check (
        Test-NoProductEvidenceAcl `
            -Paths $paths `
            -PitCrewRoot $pitCrewRoot `
            -BrokerSettings $uninstallBrokerSettings
    ) 'Uninstall left product-owned evidence ACLs behind.'
    Add-Check (
        $identityExistedBeforeUninstall -and
        (Test-Path -LiteralPath $identityRoot -PathType Container) -and
        (Test-Path -LiteralPath $preservedMarkerPath -PathType Leaf)
    ) 'Uninstall did not preserve the complete protected agent identity state.'
    if ($IsWindows) {
        Add-Check (
            $null -eq (
                Get-Service `
                    -Name PitCrewSupportAgent, PitCrewSupportBroker `
                    -ErrorAction SilentlyContinue
            )
        ) 'Uninstall left Windows support service identities installed.'
    } else {
        & getent passwd pitcrew-support-agent | Out-Null
        $agentUserExists = $LASTEXITCODE -eq 0
        & getent passwd pitcrew-support-broker | Out-Null
        $brokerUserExists = $LASTEXITCODE -eq 0
        & getent group pitcrew-support-agent | Out-Null
        $agentGroupExists = $LASTEXITCODE -eq 0
        & getent group pitcrew-support-broker | Out-Null
        $brokerGroupExists = $LASTEXITCODE -eq 0
        & getent group pitcrew-support-ipc | Out-Null
        $ipcGroupExists = $LASTEXITCODE -eq 0
        Add-Check (
            -not $agentUserExists -and
            -not $brokerUserExists -and
            -not $agentGroupExists -and
            -not $brokerGroupExists -and
            -not $ipcGroupExists
        ) 'Uninstall left Linux support service identities or IPC group installed.'
    }

    Invoke-Installer `
        -LifecycleAction 'Install' `
        -ReleaseVersion '9.9.90' `
        -Release $releaseOne `
        -PitCrewRoot $pitCrewRoot
    $installed = $true
    Invoke-Installer -LifecycleAction 'Verify'
    Add-Check (
        -not (Test-Path `
            -LiteralPath $preservedMarkerPath `
            -PathType Leaf)
    ) 'Reinstallation did not consume the preserved identity marker.'
    Invoke-Installer `
        -LifecycleAction 'Uninstall' `
        -Identity 'PreserveKeys'
    $installed = $false
} finally {
    if ($installed) {
        try {
            Invoke-Installer `
                -LifecycleAction 'Uninstall' `
                -Identity 'PreserveKeys'
        } catch {
            Write-Warning 'Product-owned support services require hosted-runner cleanup.'
        }
    }
    Remove-HostTestResidue
    if ($createdConnectorFixture) {
        Remove-Item `
            -LiteralPath $paths.ConnectorHealthRoot `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
    }
    Remove-Item `
        -LiteralPath $testRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    throw "$($errors.Count) of $checks support-plane host checks failed."
}

Write-Host "$checks support-plane host checks passed."
