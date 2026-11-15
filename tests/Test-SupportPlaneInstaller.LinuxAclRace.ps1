#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $repositoryRoot 'scripts/Install-PitCrewSupportPlane.ps1'),
    [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw 'The installer ACL race fixture requires valid installer syntax.'
}
$functionTexts = foreach ($name in @(
    'Invoke-Checked', 'Test-LinkedPathComponent', 'Get-LinuxEvidenceTreeItems',
    'Test-LinuxAtomicStagingFile', 'Test-LinuxAtomicStagingFileMissing',
    'Test-LinuxEvidenceItemPresent', 'Set-LinuxEvidenceAcl', 'Get-LinuxAclEntries',
    'Get-LinuxEffectivePermissions', 'Deny-LinuxTreeAccess', 'Assert-LinuxEvidenceAclsExact'
)) {
    ($ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $name
    }, $true)).Extent.Text
}
$module = New-Module -ArgumentList (,$functionTexts) -ScriptBlock {
    param([string[]]$Texts)
    foreach ($text in $Texts) {
        . ([scriptblock]::Create($text))
    }
    $script:linuxAgentUser = 'fixture-agent'
    $script:linuxBrokerUser = 'fixture-broker'
    $script:linuxIpcGroup = 'fixture-ipc'
    function Get-EvidencePolicy { return $script:policy }
    function Get-LinuxExpectedEvidenceAccess { return $script:contract }
    function Assert-LinuxEvidenceMetadataExact {}
    function getent {
        $global:LASTEXITCODE = 0
        return 'fixture:x:113:'
    }
    function Invoke-InjectedRemoval {
        param([string]$Path, [string]$Boundary)
        if ($Path -cne $script:culprit -or $Boundary -cne $script:boundary) {
            return $false
        }
        $script:injections++
        if ($script:mode -ceq 'parent-remove') {
            [IO.Directory]::Delete((Split-Path $Path -Parent), $true)
        } elseif ($script:mode -cne 'existing-failure') {
            [IO.File]::Delete($Path)
        }
        $global:LASTEXITCODE = 1
        return $true
    }
    function getfacl {
        param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
        $path = $Arguments[-1]
        if (Invoke-InjectedRemoval -Path $path -Boundary 'getfacl') { return }
        $global:LASTEXITCODE = 0
        $permissions = if ($script:contract.Explicit.ContainsKey($path)) {
            $script:contract.Explicit[$path]
        } elseif ((Split-Path $path -Parent) -ceq $script:evidence) { 'r--' } else { '---' }
        if ($path -ceq $script:stable) { $script:stableVerified++ }
        $mask = if ($script:mode -ceq 'mask-drift' -and $path -ceq $script:stable) {
            '---'
        } else { 'rwx' }
        "user::rwx", 'user:111:---', "user:112:$permissions",
            'group::---', "mask::$mask", 'other::---'
        if (Test-Path -LiteralPath $path -PathType Container) {
            $defaultBroker = if ($path -ceq $script:evidence) { 'r--' } else { '---' }
            'default:user::rwx', 'default:user:111:---',
                "default:user:112:$defaultBroker", 'default:group::---',
                'default:mask::rwx', 'default:other::---'
        }
    }
    function stat {
        param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
        if (Invoke-InjectedRemoval -Path $Arguments[-1] -Boundary 'stat') { return }
        $global:LASTEXITCODE = 0
        return '0:0:700'
    }
    function setfacl {
        param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
        $path = $Arguments[-1]
        if ((Test-Path -LiteralPath $path -PathType Container) -and
            $Arguments[1].Contains('d:u:')) {
            $script:defaultWrites++
        } elseif ($script:defaultWrites -ne 4) {
            throw 'Fixture observed leaf mutation before all directory denial defaults.'
        }
        if (Invoke-InjectedRemoval -Path $path -Boundary 'setfacl') { return }
        $global:LASTEXITCODE = 0
    }
    function Invoke-AclRaceFixture {
        param([string]$Root, [string]$Mode, [string]$Boundary)
        $script:mode = $Mode
        $script:boundary = $Boundary
        $script:injections = 0
        $script:stableVerified = 0
        $script:defaultWrites = 0
        $profile = Join-Path $Root '.pitcrew-state/fixture-control'
        $script:evidence = Join-Path $profile 'support-evidence'
        New-Item -ItemType Directory -Path $script:evidence -Force | Out-Null
        $script:stable = Join-Path $script:evidence 'observed-state.json'
        [IO.File]::WriteAllText($script:stable, '{}')
        $script:culprit = Join-Path $profile '.observed-state.json.2628417575.tmp'
        if ($Mode -ceq 'unknown-remove') {
            $script:culprit = Join-Path $profile '.unexpected.json.2628417575.tmp'
        } elseif ($Mode -ceq 'wrong-root-remove') {
            $script:culprit = Join-Path $Root '.observed-state.json.2628417575.tmp'
        } elseif ($Mode -ceq 'stable-remove') {
            $script:culprit = $script:stable
        }
        [IO.File]::WriteAllText($script:culprit, '{}')
        if ($Mode -ceq 'unexpected-child') {
            New-Item -ItemType Directory -Path (
                Join-Path $script:evidence '.observed-state.json.2628417575.tmp'
            ) | Out-Null
        } elseif ($Mode -ceq 'linked') {
            [IO.File]::Delete($script:culprit)
            New-Item -ItemType SymbolicLink -Path $script:culprit -Target $script:stable | Out-Null
        }
        $script:policy = [PSCustomObject]@{
            profileEvidenceDirectory = 'support-evidence'
            profileProjectionFiles = @('observed-state.json')
            connectorHealthFiles = @('connector-health.json')
        }
        $explicit = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        $explicit[$Root] = '--x'
        $explicit[(Join-Path $Root '.pitcrew-state')] = 'r-x'
        $explicit[$profile] = '--x'
        $explicit[$script:evidence] = 'r-x'
        $inherited = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        $inherited[$script:evidence] = @('observed-state.json')
        $script:contract = [PSCustomObject]@{ Explicit = $explicit; InheritedRoots = $inherited }
        $paths = @{ ConnectorHealthRoot = (Join-Path $Root 'absent-connector/health') }
        $settings = [PSCustomObject]@{
            PitCrewRoot = $Root; AllowedProfiles = 'fixture-control'
            ExpectedAgentUid = 111; BrokerUid = 112; IpcGroupGid = 113
        }
        $success = $false
        try {
            if ($Boundary -ceq 'setfacl') {
                Deny-LinuxTreeAccess -Root $Root -AgentUid 111 -BrokerUid 112 `
                    -TransientContext @{
                        PitCrewRoot = $Root; ConnectorHealthRoot = $paths.ConnectorHealthRoot
                        Policy = $script:policy
                    }
            } else {
                Assert-LinuxEvidenceAclsExact -Paths $paths -Settings $settings
            }
            $success = $true
        } catch {
            $success = $false
        }
        return [PSCustomObject]@{
            Succeeded = $success; Injections = $script:injections
            StableVerified = $script:stableVerified
            StableExists = [IO.File]::Exists($script:stable)
        }
    }
    Export-ModuleMember -Function Invoke-AclRaceFixture
}
$fixtureRoot = Join-Path $repositoryRoot ('artifacts/installer-acl-race-' + [Guid]::NewGuid().ToString('N'))
$checks = 0
try {
    # Exact injected removals, repeated without timing, sleeps or retries.
    for ($iteration = 0; $iteration -lt 3; $iteration++) {
        foreach ($boundary in @('getfacl', 'stat', 'setfacl')) {
            foreach ($mode in @('remove', 'existing-failure', 'unknown-remove', 'wrong-root-remove', 'stable-remove', 'parent-remove')) {
                $root = Join-Path $fixtureRoot "$iteration-$boundary-$mode"
                $result = & $module { param($Root, $Mode, $Boundary)
                    Invoke-AclRaceFixture -Root $Root -Mode $Mode -Boundary $Boundary
                } $root $mode $boundary
                $checks++
                if ($result.Succeeded -ne ($mode -ceq 'remove') -or $result.Injections -ne 1 -or
                    ($mode -cnotin @('stable-remove', 'parent-remove') -and -not $result.StableExists) -or
                    ($mode -ceq 'remove' -and $boundary -cne 'setfacl' -and $result.StableVerified -ne 1)) {
                    throw "Installer ACL race contract failed: $boundary/$mode."
                }
            }
        }
        foreach ($mode in @('mask-drift', 'unexpected-child', 'linked')) {
            if ($mode -ceq 'linked' -and -not $IsLinux) { continue }
            $result = & $module { param($Root, $Mode)
                Invoke-AclRaceFixture -Root $Root -Mode $Mode -Boundary 'none'
            } (Join-Path $fixtureRoot "$iteration-$mode") $mode
            $checks++
            if ($result.Succeeded) {
                throw "Installer ACL race contract hid $mode."
            }
        }
    }
} finally {
    Remove-Module $module
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
Write-Output "Support installer deterministic ACL race checks passed: $checks."
