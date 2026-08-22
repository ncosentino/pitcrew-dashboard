#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installerPath = Join-Path (
    $repositoryRoot
) 'scripts' 'Install-PitCrewSupportPlane.ps1'
$hostTestPath = Join-Path (
    $repositoryRoot
) 'tests' 'Test-SupportPlaneInstaller.ps1'
$networkProbePath = Join-Path (
    $repositoryRoot
) 'tests' 'PitCrew.Support.NetworkProbe.App' 'Program.cs'
$policyPath = Join-Path (
    $repositoryRoot
) 'assets' 'support-plane' 'support-evidence-policy-v0.10.3.json'
$connectorHealthJournalPath = Join-Path (
    $repositoryRoot
) 'src' 'PitCrew.Connector.Features.Sync' 'ConnectorHealthJournal.cs'
$agentWorkerPath = Join-Path (
    $repositoryRoot
) 'src' 'PitCrew.Support.Agent.App' 'SupportAgentWorker.cs'
$agentStartupStatusWriterPath = Join-Path (
    $repositoryRoot
) 'src' 'PitCrew.Support.Agent.App' 'SupportAgentStartupStatusWriter.cs'
$brokerRoot = Join-Path $repositoryRoot 'src' 'PitCrew.Support.Broker.App'
$agentRoot = Join-Path $repositoryRoot 'src' 'PitCrew.Support.Agent.App'
$brokerProjectPath = Join-Path $brokerRoot 'PitCrew.Support.Broker.App.csproj'
$agentProjectPath = Join-Path $agentRoot 'PitCrew.Support.Agent.App.csproj'
$errors = [Collections.Generic.List[string]]::new()
$checks = 0
$networkProbe = Get-Content -LiteralPath $networkProbePath -Raw
$connectorHealthJournal = Get-Content `
    -LiteralPath $connectorHealthJournalPath `
    -Raw
$agentWorker = Get-Content -LiteralPath $agentWorkerPath -Raw
$agentStartupStatusWriter = Get-Content `
    -LiteralPath $agentStartupStatusWriterPath `
    -Raw

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

$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $installerPath,
    [ref]$tokens,
    [ref]$parseErrors)
Add-Check (
    $parseErrors.Count -eq 0
) 'The support-plane installer has PowerShell syntax errors.'

$functions = @(
    $ast.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst]
        },
        $true
    ) |
        Select-Object -ExpandProperty Name
)
$setWindowsBrokerFirewallText = (
    $ast.Find(
        {
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Set-WindowsBrokerFirewall'
        },
        $true
    )
).Extent.Text
$invokeVerifyText = (
    $ast.Find(
        {
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-Verify'
        },
        $true
    )
).Extent.Text
$writeFailureText = (
    $ast.Find(
        {
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Write-InstallerFailureRecord'
        },
        $true
    )
).Extent.Text
foreach ($requiredFunction in @(
    'Stage-Release',
    'Remove-ObsoleteSupportVersions',
    'Enter-InstallerLock',
    'Invoke-InstallOrUpdate',
    'Invoke-Enable',
    'Invoke-Disable',
    'Invoke-Rollback',
    'Invoke-Uninstall',
    'Invoke-RepairEvidenceAcl',
    'Invoke-Verify',
    'Write-InstallerFailureRecord',
    'Get-InstallerFailureRecord',
    'Remove-InstallerFailureRecord',
    'Set-InstallerFailureContext',
    'Set-WindowsBrokerFirewall',
    'Grant-WindowsServiceParentTraversal',
    'Revoke-WindowsServiceParentTraversal',
    'Set-WindowsAgentEvidenceDeny',
    'Grant-WindowsBrokerEvidence',
    'Grant-LinuxBrokerEvidence',
    'Write-LinuxUnits',
    'Assert-EffectiveLinuxServiceBoundary',
    'Assert-LinuxCurrentVersion',
    'Assert-WindowsEvidenceAclsExact',
    'Assert-LinuxEvidenceAclsExact',
    'Assert-LinuxEvidenceMetadataExact',
    'Assert-LinuxProductGroupsRemovable',
    'Revoke-WindowsEvidenceAccess',
    'Revoke-LinuxEvidenceAccess',
    'Assert-AgentIdentityDeletionSucceeded',
    'Invoke-WindowsAgentIdentityDeletion',
    'Invoke-LinuxAgentIdentityDeletion'
)) {
    Add-Check (
        $functions -contains $requiredFunction
    ) "The installer is missing lifecycle or isolation function '$requiredFunction'."
}

$boundaryFixtureRoot = Join-Path (
    $repositoryRoot
) "tests\.support-boundary-$([Guid]::NewGuid().ToString('N'))"
$boundaryExecutionPassed = $false
try {
    $boundaryPaths = @{
        AgentInstallRoot = Join-Path $boundaryFixtureRoot 'agent-install'
        BrokerInstallRoot = Join-Path $boundaryFixtureRoot 'broker-install'
        AgentStateRoot = Join-Path $boundaryFixtureRoot 'agent-state'
        BrokerStateRoot = Join-Path $boundaryFixtureRoot 'broker-state'
        AgentUnitPath = Join-Path $boundaryFixtureRoot 'agent.service'
        BrokerUnitPath = Join-Path $boundaryFixtureRoot 'broker.service'
    }
    New-Item `
        -ItemType Directory `
        -Path $boundaryPaths.AgentStateRoot, $boundaryPaths.BrokerStateRoot `
        -Force |
        Out-Null
    $pitCrewRoot = '/opt/pitcrew'
    $agentStateArgument = '"' +
        $boundaryPaths.AgentStateRoot.Replace('\', '\\').Replace('"', '\"') +
        '"'
    $brokerStateArgument = '"' +
        $boundaryPaths.BrokerStateRoot.Replace('\', '\\').Replace('"', '\"') +
        '"'
    $commonBoundaryDirectives = @(
        'NoNewPrivileges=true'
        'PrivateDevices=true'
        'PrivateTmp=true'
        'ProtectSystem=strict'
        'ProtectKernelTunables=true'
        'ProtectKernelModules=true'
        'ProtectKernelLogs=true'
        'ProtectControlGroups=true'
        'RestrictNamespaces=true'
        'RestrictRealtime=true'
        'RestrictSUIDSGID=true'
        'LockPersonality=true'
        'CapabilityBoundingSet='
        'AmbientCapabilities='
    )
    [IO.File]::WriteAllText(
        $boundaryPaths.AgentUnitPath,
        (@(
            "WorkingDirectory=$($boundaryPaths.AgentStateRoot)"
            "Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=$(Join-Path $boundaryPaths.AgentStateRoot 'bundle')"
            'RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6'
            "ReadWritePaths=$agentStateArgument"
            'UMask=0077'
            'ProtectHome=true'
        ) + $commonBoundaryDirectives) -join "`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $boundaryPaths.BrokerUnitPath,
        (@(
            "WorkingDirectory=$($boundaryPaths.BrokerStateRoot)"
            "Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=$(Join-Path $boundaryPaths.BrokerStateRoot 'bundle')"
            'PrivateNetwork=true'
            'RestrictAddressFamilies=AF_UNIX'
            'IPAddressDeny=any'
            'RuntimeDirectory=pitcrew-support'
            'RuntimeDirectoryMode=0750'
            "ReadWritePaths=/run/pitcrew-support $brokerStateArgument"
            "BindReadOnlyPaths=`"$pitCrewRoot`""
            'UMask=0007'
            'ProtectHome=tmpfs'
        ) + $commonBoundaryDirectives) -join "`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $boundaryPaths.BrokerStateRoot 'appsettings.json'),
        (@{
            PitCrewSupport = @{
                Broker = @{
                    PitCrewRoot = $pitCrewRoot
                }
            }
        } | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))

    $linuxAgentService = 'pitcrew-support-agent.service'
    $linuxBrokerService = 'pitcrew-support-broker.service'
    $linuxAgentUser = 'pitcrew-support-agent'
    $linuxBrokerUser = 'pitcrew-support-broker'
    $linuxIpcGroup = 'pitcrew-support-ipc'
    $socketPath = '/run/pitcrew-support/broker.sock'
    $agentExecutable = Join-Path (
        Join-Path $boundaryPaths.AgentInstallRoot 'current'
    ) 'PitCrew.Support.Agent.App'
    $brokerExecutable = Join-Path (
        Join-Path $boundaryPaths.BrokerInstallRoot 'current'
    ) 'PitCrew.Support.Broker.App'
    $agentReplayRoot = Join-Path $boundaryPaths.AgentStateRoot 'replay'
    $agentCommand =
        "$agentExecutable --contentRoot $($boundaryPaths.AgentStateRoot) --PitCrewSupport:Agent:SocketPath=$socketPath --PitCrewSupport:Agent:ReplayRoot=$agentReplayRoot"
    $brokerCommand =
        "$brokerExecutable --contentRoot $($boundaryPaths.BrokerStateRoot)"
    $script:effectiveSystemdProperties =
        [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::Ordinal)

    function Set-EffectiveSystemdProperty {
        param(
            [Parameter(Mandatory)][string]$Unit,
            [Parameter(Mandatory)][string]$Property,
            [AllowEmptyString()][string]$Value
        )

        $script:effectiveSystemdProperties["$Unit|$Property"] = $Value
    }

    foreach ($property in @(
        'Environment',
        'EnvironmentFiles',
        'PassEnvironment',
        'ExecCondition',
        'ExecStartPre',
        'ExecStartPost',
        'ExecReload',
        'ExecStop',
        'ExecStopPost',
        'RootDirectory',
        'RootImage',
        'BindPaths',
        'CapabilityBoundingSet',
        'AmbientCapabilities',
        'DropInPaths'
    )) {
        Set-EffectiveSystemdProperty `
            -Unit $linuxAgentService `
            -Property $property `
            -Value ''
        Set-EffectiveSystemdProperty `
            -Unit $linuxBrokerService `
            -Property $property `
            -Value ''
    }
    foreach ($property in @(
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
        Set-EffectiveSystemdProperty `
            -Unit $linuxAgentService `
            -Property $property `
            -Value 'yes'
        Set-EffectiveSystemdProperty `
            -Unit $linuxBrokerService `
            -Property $property `
            -Value 'yes'
    }
    foreach ($entry in @(
        @($linuxAgentService, 'User', $linuxAgentUser),
        @($linuxAgentService, 'Group', $linuxAgentUser),
        @($linuxAgentService, 'SupplementaryGroups', $linuxIpcGroup),
        @($linuxAgentService, 'FragmentPath', $boundaryPaths.AgentUnitPath),
        @($linuxAgentService, 'ExecStart',
            "{ path=$agentExecutable ; argv[]=$agentCommand ; }"),
        @($linuxAgentService, 'WorkingDirectory',
            $boundaryPaths.AgentStateRoot),
        @($linuxAgentService, 'PrivateNetwork', 'no'),
        @($linuxAgentService, 'RestrictAddressFamilies',
            'AF_INET AF_INET6 AF_UNIX'),
        @($linuxAgentService, 'IPAddressDeny', ''),
        @($linuxAgentService, 'IPAddressAllow', ''),
        @($linuxAgentService, 'ReadWritePaths',
            $boundaryPaths.AgentStateRoot),
        @($linuxAgentService, 'RuntimeDirectory', ''),
        @($linuxAgentService, 'UMask', '0077'),
        @($linuxAgentService, 'ProtectHome', 'yes'),
        @($linuxAgentService, 'BindReadOnlyPaths', ''),
        @($linuxAgentService, 'ProtectSystem', 'strict'),
        @($linuxBrokerService, 'User', $linuxBrokerUser),
        @($linuxBrokerService, 'Group', $linuxIpcGroup),
        @($linuxBrokerService, 'SupplementaryGroups', $linuxBrokerUser),
        @($linuxBrokerService, 'FragmentPath', $boundaryPaths.BrokerUnitPath),
        @($linuxBrokerService, 'ExecStart',
            "{ path=$brokerExecutable ; argv[]=$brokerCommand ; }"),
        @($linuxBrokerService, 'WorkingDirectory',
            $boundaryPaths.BrokerStateRoot),
        @($linuxBrokerService, 'PrivateNetwork', 'yes'),
        @($linuxBrokerService, 'RestrictAddressFamilies', 'AF_UNIX'),
        @($linuxBrokerService, 'IPAddressDeny', 'any'),
        @($linuxBrokerService, 'IPAddressAllow', ''),
        @($linuxBrokerService, 'ReadWritePaths',
            "/run/pitcrew-support $($boundaryPaths.BrokerStateRoot)"),
        @($linuxBrokerService, 'RuntimeDirectory', 'pitcrew-support'),
        @($linuxBrokerService, 'RuntimeDirectoryMode', '0750'),
        @($linuxBrokerService, 'UMask', '0007'),
        @($linuxBrokerService, 'ProtectHome', 'tmpfs'),
        @($linuxBrokerService, 'BindReadOnlyPaths', $pitCrewRoot),
        @($linuxBrokerService, 'ProtectSystem', 'strict')
    )) {
        Set-EffectiveSystemdProperty `
            -Unit $entry[0] `
            -Property $entry[1] `
            -Value $entry[2]
    }

    function systemctl {
        $arguments = @($args)
        $propertyArgument = @(
            $arguments |
                Where-Object {
                    $_.StartsWith(
                        '--property=',
                        [StringComparison]::Ordinal)
                }
        )
        $unitArgument = @(
            $arguments |
                Where-Object {
                    $_ -cne 'show' -and
                    $_ -cne '--value' -and
                    -not $_.StartsWith(
                        '--property=',
                        [StringComparison]::Ordinal)
                }
        )
        if ($arguments.Count -ne 4 -or
            $arguments[0] -cne 'show' -or
            $propertyArgument.Count -ne 1 -or
            $unitArgument.Count -ne 1) {
            throw 'The focused systemd verifier received an invalid command.'
        }
        $property = $propertyArgument[0].Substring(
            '--property='.Length)
        $key = "$($unitArgument[0])|$property"
        if (-not $script:effectiveSystemdProperties.ContainsKey($key)) {
            throw "The focused systemd verifier omitted '$key'."
        }
        $global:LASTEXITCODE = 0
        return $script:effectiveSystemdProperties[$key]
    }

    $boundaryFunctionNames = @(
        'ConvertTo-SystemdArgument',
        'Get-SystemdProperty',
        'Assert-SystemdProperty',
        'Assert-SystemdSetProperty',
        'Assert-SystemdUnitDirective',
        'Assert-SystemdUnitDirectiveAbsent',
        'Assert-SystemdExecStart',
        'Assert-EffectiveLinuxServiceBoundary'
    )
    foreach ($functionAst in $ast.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst]
        },
        $true
    ) | Where-Object {
        $_.Name -in $boundaryFunctionNames
    }) {
        . ([scriptblock]::Create($functionAst.Extent.Text))
    }

    Assert-EffectiveLinuxServiceBoundary -Paths $boundaryPaths
    $boundaryExecutionPassed = $true
} catch {
    $errors.Add(
        "Valid strict-mode systemd verification failed: $($_.Exception.Message)")
} finally {
    Remove-Item `
        -LiteralPath $boundaryFixtureRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
    Remove-Item Function:\systemctl -ErrorAction SilentlyContinue
    Remove-Item Function:\Set-EffectiveSystemdProperty `
        -ErrorAction SilentlyContinue
}
Add-Check (
    $boundaryExecutionPassed
) 'Valid effective systemd properties do not execute under strict mode.'

$installer = Get-Content -LiteralPath $installerPath -Raw -Encoding UTF8
$hostTest = Get-Content -LiteralPath $hostTestPath -Raw -Encoding UTF8
$brokerProject = Get-Content `
    -LiteralPath $brokerProjectPath `
    -Raw `
    -Encoding UTF8
$agentProject = Get-Content `
    -LiteralPath $agentProjectPath `
    -Raw `
    -Encoding UTF8
Add-Check (
    $brokerProject.Contains(
        '<NeedlrAutoGenerate>false</NeedlrAutoGenerate>',
        [StringComparison]::Ordinal) -and
    $agentProject.Contains(
        '<PackageReference Include="NexusLabs.Needlr.Generators.Attributes" />',
        [StringComparison]::Ordinal)
) 'Support single-file apps do not pin their required Needlr runtime behavior.'
foreach ($action in @(
    "'Install'",
    "'Update'",
    "'Enable'",
    "'Disable'",
    "'Uninstall'",
    "'Rollback'",
    "'RepairEvidenceAcl'",
    "'Verify'"
)) {
    Add-Check (
        $installer.Contains($action, [StringComparison]::Ordinal)
    ) "The installer does not expose action $action."
}
Add-Check (
    $installer.Contains(
        "[ValidateSet('PreserveKeys', 'DeleteKeys')]",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "'identity-delete-keys'",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "'delete-keys-succeeded'",
        [StringComparison]::Ordinal)
) 'Explicit service-identity DeleteKeys uninstall is unavailable.'
Add-Check (
    $installer.Contains(
        "[ValidateSet('PreserveKeys')]",
        [StringComparison]::Ordinal)
) 'The installer does not require explicit protected identity preservation.'
Add-Check (
    $installer.Contains(
        '[System.IO.UnixFileMode]::UserRead',
        [StringComparison]::Ordinal)
) 'The installer uses an unqualified UnixFileMode type that fails in PowerShell.'
Add-Check (
    $installer -match '''binPath='',\s*\r?\n\s*\$binaryPath' -and
    $installer -notmatch '"binPath= \$binaryPath"'
) 'The installer does not pass sc.exe option names and values separately.'
Add-Check (
    $hostTest.Contains(
        "& sc.exe config PitCrewSupportBroker 'binPath=' `$probeCommand",
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        "& sc.exe config PitCrewSupportBroker 'binPath=' `$originalPath",
        [StringComparison]::Ordinal) -and
    -not $hostTest.Contains(
        '"binPath= $originalPath"',
        [StringComparison]::Ordinal)
) 'The Windows network probe does not preserve quoted broker commands as separate sc.exe arguments.'
Add-Check (
    $installer.Contains(
        '"*$AgentSid`:(X,RA)"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Revoke-WindowsServiceParentTraversal',
        [StringComparison]::Ordinal)
) 'Windows service identities lack bounded parent traversal lifecycle.'
Add-Check (
    $installer.Contains(
        'Sort-Object { $_.FullName.Length } -Descending',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '"NT SERVICE\$Name"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '[switch]$Recurse',
        [StringComparison]::Ordinal)
) 'Windows service images are not protected child-first for virtual service identities.'
Add-Check (
    $installer -match '(?ms)^\s*Set-WindowsProtectedDirectoryAcl `\r?\n\s+-Path \$Paths\.AgentInstallRoot `\r?\n\s+-ServiceSid \$agentSid `\r?\n\s+-ServiceRights ''RX'' `\r?\n\s+-Recurse\s*$' -and
    $installer -match '(?ms)^\s*Set-WindowsProtectedDirectoryAcl `\r?\n\s+-Path \$Paths\.BrokerInstallRoot `\r?\n\s+-ServiceSid \$brokerSid `\r?\n\s+-ServiceRights ''RX'' `\r?\n\s+-Recurse\s*$' -and
    $installer -match '(?ms)^\s*Set-WindowsProtectedDirectoryAcl `\r?\n\s+-Path \$Paths\.AgentStateRoot `\r?\n\s+-ServiceSid \$agentSid `\r?\n\s+-ServiceRights ''F''\s*$' -and
    $installer -match '(?ms)^\s*Set-WindowsProtectedDirectoryAcl `\r?\n\s+-Path \$Paths\.BrokerStateRoot `\r?\n\s+-ServiceSid \$brokerSid `\r?\n\s+-ServiceRights ''F''\s*$'
) 'Windows state-root ACL application can recurse into protected identity storage.'
Add-Check (
    $installer.Contains(
        "@('sidtype', `$Name, 'unrestricted')",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "'SeImpersonatePrivilege'",
        [StringComparison]::Ordinal)
) 'Windows service SID or named-pipe impersonation privileges are incomplete.'
Add-Check (
    $installer.Contains(
        'DOTNET_BUNDLE_EXTRACT_BASE_DIR=',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "New-ItemProperty",
        [StringComparison]::Ordinal)
) 'Windows services do not use protected single-file extraction roots.'
Add-Check (
$installer.Contains(
    'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)',
    [StringComparison]::Ordinal)
) 'Windows service lifecycle rights depend on an image-default service DACL.'
Add-Check (
$installer.Contains(
    '"NT SERVICE\$Name"',
    [StringComparison]::Ordinal) -and
$installer.Contains(
    "@('sidtype', `$Name, 'unrestricted')",
    [StringComparison]::Ordinal)
) 'Windows services do not use distinct unrestricted virtual service identities.'
Add-Check (
    $installer.Contains(
        '"u:$AgentUid`:---,u:$BrokerUid`:---"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '"d:u:$AgentUid`:---,d:u:$BrokerUid`:---"',
        [StringComparison]::Ordinal)
) 'The installer passes multiple setfacl modification specs as filenames.'
Add-Check (
    $installer.Contains(
        '-EvidenceRoot ([IO.Path]::GetPathRoot($ResolvedPitCrewRoot))',
        [StringComparison]::Ordinal)
) 'The installer does not grant execute-only traversal to the selected PitCrew root.'
Add-Check (
    $installer.Contains(
        'PrivateNetwork=true',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'RestrictAddressFamilies=AF_UNIX',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'IPAddressDeny=any',
        [StringComparison]::Ordinal)
) 'The Linux broker unit does not structurally remove network access.'
Add-Check (
    $installer.Contains(
        'Invoke-Checked systemd-analyze @(',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$Paths.AgentUnitPath,',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$Paths.BrokerUnitPath',
        [StringComparison]::Ordinal)
) 'Linux units are not parser-verified before service installation.'
Add-Check (
    $installer.Contains(
        '$targets = @($item.Target)',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '[IO.Path]::IsPathRooted($target)',
        [StringComparison]::Ordinal) -and
    -not $installer.Contains(
        '(Resolve-Path -LiteralPath $current).Path',
        [StringComparison]::Ordinal)
) 'Linux current-version verification does not compare the normalized symlink target.'
Add-Check (
    $installer.Contains(
        "-Property 'UnitFileState'",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$expectedUnitFileState',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Bounded diagnostics: UnitFileState=$unitFileState;ActiveState=$activeState',
        [StringComparison]::Ordinal)
) 'Linux lifecycle verification relies on ambiguous systemctl exit codes.'
Add-Check (
    $installer.Contains(
        'function Wait-LinuxSupportServiceActive',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '($stopwatch.Elapsed - $activeSince).TotalSeconds -ge 1',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'did not stabilize as active. Bounded diagnostics: $diagnostics',
        [StringComparison]::Ordinal)
) 'Linux service startup accepts transitional activation or lacks bounded failure diagnostics.'
Add-Check (
    $installer.Contains(
        "-Direction Outbound",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "-Action Block",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "-Service `$windowsBrokerService",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '-LocalUser "D:(A;;CC;;;$brokerSid)"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Get-NetFirewallSecurityFilter',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Get-NetFirewallApplicationFilter',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '-Program $Executable',
        [StringComparison]::Ordinal) -and
    $setWindowsBrokerFirewallText.Contains(
        '$programRule = Get-NetFirewallRule -Name $windowsProgramFirewallRule',
        [StringComparison]::Ordinal) -and
    $setWindowsBrokerFirewallText.Contains(
        '$applicationFilter = $programRule | Get-NetFirewallApplicationFilter',
        [StringComparison]::Ordinal) -and
    $invokeVerifyText.Contains(
        '$programRule = Get-NetFirewallRule -Name $windowsProgramFirewallRule',
        [StringComparison]::Ordinal) -and
    $invokeVerifyText.Contains(
        '$applicationFilter = $programRule | Get-NetFirewallApplicationFilter',
        [StringComparison]::Ordinal)
) 'Windows firewall creation and verification do not both resolve the exact program rule.'
Add-Check (
    $installer.Contains(
        '$rule.Enabled -ne ''True''',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'Disable-NetFirewallRule',
        [StringComparison]::Ordinal)
) 'Disabled Windows firewall rules are not rejected by creation and host verification.'
Add-Check (
    $installer.Contains(
        '$windowsAgentService = ''PitCrewSupportAgent''',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$windowsBrokerService = ''PitCrewSupportBroker''',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$linuxAgentUser = ''pitcrew-support-agent''',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$linuxBrokerUser = ''pitcrew-support-broker''',
        [StringComparison]::Ordinal)
) 'Agent and broker product identities are not separately fixed.'
Add-Check (
    $installer.Contains(
        'An unmanaged or partial privileged support installation already exists.',
        [StringComparison]::Ordinal)
) 'The installer does not refuse ambiguous privileged support installations.'
Add-Check (
    $installer.Contains(
        'PitCrew and connector-health evidence roots must not overlap.',
        [StringComparison]::Ordinal)
) 'The installer does not reject overlapping evidence trust roots.'
Add-Check (
    $installer.Contains(
        '''DiagnoseFailure''',
        [StringComparison]::Ordinal) -and
    $writeFailureText.Contains(
        'phase = $script:installerFailurePhase',
        [StringComparison]::Ordinal) -and
    $writeFailureText.Contains(
        'operation = $script:installerFailureOperation',
        [StringComparison]::Ordinal) -and
    $writeFailureText.Contains(
        'nativeExitCode = $primaryNativeExitCode',
        [StringComparison]::Ordinal) -and
    $writeFailureText.Contains(
        'rollbackStatus = $script:installerRollbackStatus',
        [StringComparison]::Ordinal) -and
    -not $writeFailureText.Contains(
        '.Message',
        [StringComparison]::Ordinal) -and
    -not $writeFailureText.Contains(
        'StackTrace',
        [StringComparison]::Ordinal)
) 'The installer does not persist and expose a bounded secret-free failure journal.'
Add-Check (
    $installer.Contains(
        "'agent-startup-status.json'",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'startupDisposition=',
        [StringComparison]::Ordinal) -and
    $agentWorker.Contains(
        '"identity-unavailable"',
        [StringComparison]::Ordinal) -and
    $agentWorker.Contains(
        '"local-enrollment-commit-failed"',
        [StringComparison]::Ordinal) -and
    $agentWorker.Contains(
        '"enrollment-rejected"',
        [StringComparison]::Ordinal) -and
    $agentWorker.Contains(
        '"unhandled-exception"',
        [StringComparison]::Ordinal) -and
    $agentWorker.Contains(
        '_startupStatus.Clear()',
        [StringComparison]::Ordinal) -and
    $agentStartupStatusWriter.Contains(
        'exceptionType?.Name',
        [StringComparison]::Ordinal) -and
    -not $agentStartupStatusWriter.Contains(
        '.Message',
        [StringComparison]::Ordinal) -and
    -not $agentStartupStatusWriter.Contains(
        'StackTrace',
        [StringComparison]::Ordinal)
) 'Stopped support-agent startup cannot be diagnosed through a bounded secret-free status.'
foreach ($operation in @(
    'verify-exact-evidence-acls',
    'enumerate-evidence-tree',
    'reject-unexpected-evidence-directory',
    'reject-unexpected-evidence-file',
    'reject-unexpected-broker-ace',
    'verify-broker-ace-count',
    'verify-broker-ace-shape',
    'verify-agent-deny-count',
    'verify-agent-deny-shape',
    'verify-agent-root-deny',
    'verify-state-root-enumeration',
    'verify-root-metadata-acl',
    'verify-evidence-read-acl',
    'verify-agent-evidence-deny',
    'verify-prohibited-environment-deny'
)) {
    Add-Check (
        $installer.Contains(
            "-Operation '$operation'",
            [StringComparison]::Ordinal)
    ) "The installer failure journal does not distinguish '$operation'."
}
Add-Check (
    $installer.Contains(
        '"*$BrokerSid`:(RD,X,RA)"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '"*$BrokerSid`:(OI)(IO)(R)"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '"u:$BrokerUid`:r-x"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '"u:$BrokerUid`:r-x,d:u:$BrokerUid`:r--"',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '"*$BrokerSid`:(RA)"',
        [StringComparison]::Ordinal)
) 'The installer does not separate profile enumeration from dedicated inherited evidence read.'
Add-Check (
    $connectorHealthJournal.Contains(
        'var directoryCreated = !Directory.Exists(directory);',
        [StringComparison]::Ordinal) -and
    $connectorHealthJournal.Contains(
        'if (!OperatingSystem.IsWindows() && directoryCreated)',
        [StringComparison]::Ordinal) -and
    $connectorHealthJournal.Contains(
        'UnixFileMode.GroupRead',
        [StringComparison]::Ordinal)
) 'Connector-health atomic replacement can reset the installed directory ACL or mask broker file read.'
Add-Check (
    $installer.Contains(
        'systemctl show',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'DropInPaths',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Unexpected effective systemd DropInPaths were detected.',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'BindReadOnlyPaths',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Assert-EffectiveLinuxServiceBoundary',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'BindPaths=/etc',
        [StringComparison]::Ordinal)
) 'Effective systemd properties or overriding drop-ins lack negative coverage.'
$effectiveBoundaryText = @(
    $ast.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Assert-EffectiveLinuxServiceBoundary'
        },
        $true
    )
)[0].Extent.Text
$dropInCheckIndex = $effectiveBoundaryText.IndexOf(
    "-Property 'DropInPaths'",
    [StringComparison]::Ordinal)
$firstOverridableCheckIndex = $effectiveBoundaryText.IndexOf(
    "-Property 'User'",
    [StringComparison]::Ordinal)
Add-Check (
    $dropInCheckIndex -ge 0 -and
    $firstOverridableCheckIndex -ge 0 -and
    $dropInCheckIndex -lt $firstOverridableCheckIndex -and
    $hostTest.Contains(
        '$_.Exception.Message.Equals(',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'Unexpected effective systemd DropInPaths were detected.',
        [StringComparison]::Ordinal)
) 'Drop-in rejection does not precede all overridable systemd property checks.'
Add-Check (
    $installer.Contains(
        'Assert-WindowsEvidenceAclsExact',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Assert-LinuxEvidenceAclsExact',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'getfacl',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'evidence-metadata-v0.10.3.json',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        ':(OI)(CI)F',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'pitcrew-support-broker:rwx',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'chmod o+w',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'mode drift',
        [StringComparison]::Ordinal)
) 'Exact ACL verification lacks writable or inherited/default drift coverage.'
Add-Check (
    $installer.Contains(
        'Revoke-WindowsEvidenceAccess',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Revoke-LinuxEvidenceAccess',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Remove-LinuxProductIdentities',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'Test-NoProductEvidenceAcl',
        [StringComparison]::Ordinal)
) 'Uninstall does not prove evidence ACL and service-identity revocation.'
Add-Check (
    $installer.Contains(
        'Assert-LinuxProductGroupsRemovable',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'external supplementary member',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'external account primary group',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'pitcrew-support-test-member',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'pitcrew-support-test-primary',
        [StringComparison]::Ordinal)
) 'Uninstall lacks pre-mutation external product-group ownership coverage.'
Add-Check (
    $installer.Contains(
        'Enter-InstallerLock',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '[IO.FileShare]::None',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '/run/lock/pitcrew-support-plane/lifecycle.lock',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        "Invoke-Checked chmod @('700', `$lockRoot)",
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'concurrent privileged lifecycle',
        [StringComparison]::Ordinal)
) 'Lifecycle actions are not covered by a privileged concurrency lock.'
Add-Check (
    $installer -match '(?s)function Preserve-AgentIdentityState.*?if \(\$IsWindows\).*?icacls\.exe.*?\} else \{.*?chown.*?function Invoke-Uninstall' -and
    $installer -notmatch '(?s)function Preserve-AgentIdentityState.*?Protect-StateRootsForInstaller.*?function Invoke-Uninstall' -and
    $installer.Contains(
        'function Get-IdentityPreservationMarkerPath',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '(Split-Path $Paths.AgentStateRoot -Parent)',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '$removeInstallerStateAfterUnlock = $true',
        [StringComparison]::Ordinal) -and
    $installer -match '(?s)\} finally \{\s*\$installerLock\.Dispose\(\)\s*\}\s*if \(\$removeInstallerStateAfterUnlock\)' -and
    $installer -notmatch '(?s)-LiteralPath \$Paths\.BrokerStateRoot, \$Paths\.InstallerStateRoot'
) 'Windows identity preservation or installer-state cleanup crosses the lifecycle lock boundary.'
Add-Check (
    $installer.Contains(
        '$hasPreservedIdentity = -not $IsUpdate',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        '} elseif (-not $hasPreservedIdentity) {',
        [StringComparison]::Ordinal)
) 'Reinstallation can recursively reprotect an existing preserved identity subtree.'
Add-Check (
    $installer.Contains(
        "-not `$PSBoundParameters.ContainsKey('IdentityHandling')",
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Uninstall requires an explicit -IdentityHandling PreserveKeys choice.',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        '$missingIdentityHandlingRejected',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        "-Action 'Uninstall'",
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        "-Identity 'PreserveKeys'",
        [StringComparison]::Ordinal)
) 'Uninstall does not require and test an explicit identity-handling choice.'
Add-Check (
    $hostTest.Contains(
        'Invoke-InstalledBrokerNetworkDenialProbe',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        "[Net.Dns]::GetHostAddresses('example.com')",
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'Test-PublicTcpConnection -Address $address -Port $port',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'ExecStart=/usr/bin/python3 $probePath $resultPath $($controlEndpoint.Address) $($controlEndpoint.Port)',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        '--address `"$($controlEndpoint.Address)`"',
        [StringComparison]::Ordinal) -and
    $networkProbe.Contains(
        'System.Net.IPAddress.TryParse(address',
        [StringComparison]::Ordinal) -and
    $networkProbe.Contains(
        '_options.Address',
        [StringComparison]::Ordinal) -and
    $networkProbe.Contains(
        '_options.Port',
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        "'denied'",
        [StringComparison]::Ordinal) -and
    $hostTest.Contains(
        'The Linux broker network probe produced no result.',
        [StringComparison]::Ordinal)
) 'Hosted installer tests do not attempt and reject public outbound broker access.'
Add-Check (
    $installer.Contains(
        'staging-',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'previousVersion',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Configure-WindowsVersion',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Configure-LinuxVersion',
        [StringComparison]::Ordinal)
) 'Update staging and rollback switching are incomplete.'
Add-Check (
    $installer.Contains(
        'Binary updates cannot change the locally selected PitCrew root or profiles.',
        [StringComparison]::Ordinal) -and
    $installer.Contains(
        'Remove-ObsoleteSupportVersions',
        [StringComparison]::Ordinal)
) 'Updates do not preserve the installed evidence boundary or bounded rollback set.'

$policy = Get-Content `
    -LiteralPath $policyPath `
    -Raw `
    -Encoding UTF8 |
    ConvertFrom-Json -Depth 10
Add-Check (
    $policy.schemaVersion -eq 2 -and
    $policy.pitCrewVersion -eq '0.10.3'
) 'The evidence ACL policy is not pinned to PitCrew v0.10.3 contract v2.'
Add-Check (
    $policy.pitCrewCommit -eq
        '4fbafcafca1aa659a07b2f5deb96edc5d3eb3269'
) 'The evidence ACL policy is not pinned to the verified PitCrew v0.10.3 collector commit.'
Add-Check (
    $policy.collectorSha256 -eq
        '18ed0cdb53e288f981bf5cc49cb404a5129b98ac14faaa5a6cbcab07b3591580' -and
    $policy.collectorHashCanonicalization -eq 'utf8-lf' -and
    $installer.Contains(
        'Get-CanonicalTextSha256 -LiteralPath $collector',
        [StringComparison]::Ordinal)
) 'The fixed collector content is not cryptographically pinned by policy and installer.'
Add-Check (
    $policy.profileStateRootAccess -eq
        'enumerate-profile-directories-only' -and
    $policy.profileEvidenceDirectory -eq 'support-evidence' -and
    $policy.windowsEvidenceInheritance -eq
        'object-inherit-read-ace' -and
    $policy.linuxEvidenceInheritance -eq
        'directory-read-and-default-file-read-acl'
) 'The evidence ACL policy does not model dedicated evidence-directory inheritance.'
Add-Check (
    (@($policy.profileProjectionFiles) -join ',') -eq
        'desired-capacity.json,acknowledged-capacity.json,static-profile.json,observed-state.json'
) 'The profile projection allowlist is not exact.'
Add-Check (
    (@($policy.connectorHealthFiles) -join ',') -eq
        'connector-health.json,connector-events.jsonl,connector-health-acknowledgement.json'
) 'The connector-health allowlist is not exact.'
$policyText = $policy | ConvertTo-Json -Depth 10 -Compress
Add-Check (
    $policyText -notmatch '\.env|docker\.sock'
) 'The evidence policy contains a prohibited environment or Docker path.'

$ipcSources = @(
    Get-ChildItem -LiteralPath $brokerRoot, $agentRoot -Filter '*.cs' -File |
        Get-Content -Raw
) -join "`n"
Add-Check (
    $ipcSources -notmatch 'CurrentUserOnly'
) 'The runtime still relies on PipeOptions.CurrentUserOnly.'
Add-Check (
    $ipcSources -match 'SO_PEERCRED|SoPeerCred' -and
    $ipcSources -match 'ExpectedAgentUid'
) 'The Unix socket path does not verify peer credentials.'
Add-Check (
    $ipcSources -match 'RunAsClient' -and
    $ipcSources -match 'ExpectedAgentSid'
) 'The named-pipe path does not validate the impersonated client SID.'

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    throw "$($errors.Count) of $checks support-plane structural checks failed."
}

Write-Host "$checks support-plane structural checks passed."
