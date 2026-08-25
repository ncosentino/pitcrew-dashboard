#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$modulePath = Join-Path (
    $repositoryRoot
) 'scripts' 'release' 'SupportReleaseGate.psm1'
$policyPath = Join-Path (
    $repositoryRoot
) 'assets' 'support-plane' 'support-evidence-policy-v0.10.8.json'
$prepareWorkflowPath = Join-Path (
    $repositoryRoot
) '.github' 'workflows' 'prepare-release.yml'
$verifyWorkflowPath = Join-Path (
    $repositoryRoot
) '.github' 'workflows' 'verify-support-release-gate.yml'
$supportCanaryWorkflowPath = Join-Path (
    $repositoryRoot
) '.github' 'workflows' 'support-canary.yml'
$publishedVerificationWorkflowPath = Join-Path (
    $repositoryRoot
) '.github' 'workflows' 'verify-published-release.yml'
$publishedContainerVerifierPath = Join-Path (
    $repositoryRoot
) 'scripts' 'release' 'Test-PublishedContainerImage.ps1'
$ciWorkflowPath = Join-Path (
    $repositoryRoot
) '.github' 'workflows' 'ci.yml'
$supportRelayScenarioPath = Join-Path (
    $repositoryRoot
) 'scripts' 'canary' 'Invoke-SupportRelayScenario.ps1'
$canaryScenarioEntryPaths = @(
    Join-Path $repositoryRoot 'scripts' 'canary' 'Invoke-SupportCanary.ps1'
    Join-Path $repositoryRoot 'scripts' 'canary' 'Invoke-SupportCanaryScenario.ps1'
    Join-Path $repositoryRoot 'scripts' 'canary' 'New-SupportCanaryRun.ps1'
)
$canaryTopologyEntryPaths = @(
    Join-Path $repositoryRoot 'scripts' 'canary' 'Invoke-SupportCanary.ps1'
    Join-Path $repositoryRoot 'scripts' 'canary' 'New-SupportCanaryRun.ps1'
    Join-Path $repositoryRoot 'scripts' 'canary' 'SupportCanary.Common.ps1'
)
$canaryBuildPath = Join-Path (
    $repositoryRoot
) 'scripts' 'canary' 'Build-SupportCanary.ps1'
$publishWorkflowPaths = @(
    Join-Path $repositoryRoot '.github' 'workflows' 'publish-container.yml'
    Join-Path $repositoryRoot '.github' 'workflows' 'publish-host-connector.yml'
    Join-Path $repositoryRoot '.github' 'workflows' 'publish-support-plane.yml'
)
$errors = [Collections.Generic.List[string]]::new()
$checks = 0

function Add-Check {
    param(
        [object] $Condition,
        [Parameter(Mandatory)]
        [string] $Failure
    )

    $script:checks++
    if (-not [bool]$Condition) {
        $script:errors.Add($Failure)
    }
}

function Add-RejectionCheck {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Operation
    )

    $script:checks++
    try {
        & $Operation
        $script:errors.Add("$Name was accepted.")
    } catch {
        Write-Verbose "$Name rejected with $($_.Exception.GetType().Name)."
    }
}

Import-Module $modulePath -Force

$policy = Get-Content -LiteralPath $policyPath -Raw -Encoding utf8 |
    ConvertFrom-Json -Depth 20
$dashboardSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$pitCrewSha = [string]$policy.pitCrewCommit
$runId = 123456789
$marker = New-SupportReleaseGateMarker `
    -ReleaseTag 'v1.2.3' `
    -DashboardSha $dashboardSha `
    -PitCrewSha $pitCrewSha `
    -RunId $runId
$releaseBody = "$marker`n`n## Generated notes`n"

function New-ReleaseFixture {
    param(
        [string] $Body = $script:releaseBody,
        [string] $Author = 'github-actions[bot]',
        [string] $Target = $script:dashboardSha,
        [bool] $Draft = $false,
        [bool] $Prerelease = $false,
        [string] $Tag = 'v1.2.3'
    )

    return [pscustomobject]@{
        tag_name = $Tag
        target_commitish = $Target
        draft = $Draft
        prerelease = $Prerelease
        body = $Body
        author = [pscustomobject]@{
            login = $Author
        }
    }
}

function New-WorkflowRunFixture {
    param(
        [long] $Id = $script:runId,
        [AllowEmptyString()]
        [string] $Name = '',
        [string] $Tag = 'v1.2.3',
        [string] $Path = '.github/workflows/prepare-release.yml',
        [string] $Event = 'workflow_dispatch',
        [string] $Branch = 'main',
        [string] $Sha = $script:dashboardSha,
        [string] $Status = 'completed',
        [string] $Conclusion = 'success',
        [string] $Repository = 'ncosentino/pitcrew-dashboard'
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = "Prepare $Tag from $script:dashboardSha"
    }

    return [pscustomobject]@{
        id = $Id
        name = $Name
        path = $Path
        event = $Event
        head_branch = $Branch
        head_sha = $Sha
        status = $Status
        conclusion = $Conclusion
        repository = [pscustomobject]@{
            full_name = $Repository
        }
    }
}

function New-WorkflowJobsFixture {
    param(
        [string] $Status = 'completed',
        [string] $Conclusion = 'success',
        [AllowEmptyCollection()]
        [string[]] $Name = @()
    )

    $names = if ($Name.Count -eq 0) {
        @(
            'Gate release candidate / Portable support canary'
            'Gate containerized candidate / Containerized support canary'
            'Gate installed Windows candidate / Windows-installed support canary'
            'Gate installed Linux candidate / Linux-installed support canary'
            'Create gated draft release'
        )
    } else {
        @($Name)
    }
    return [pscustomobject]@{
        jobs = @(
            foreach ($jobName in $names) {
                [pscustomobject]@{
                    name = $jobName
                    status = $Status
                    conclusion = $Conclusion
                }
            }
        )
    }
}

$parsedMarker = Read-SupportReleaseGateMarker -ReleaseBody $releaseBody
Add-Check ($parsedMarker.DashboardSha -ceq $dashboardSha) (
    'Release marker did not preserve the Dashboard SHA.')
Add-Check ($parsedMarker.ReleaseTag -ceq 'v1.2.3') (
    'Release marker did not preserve the release tag.')
Add-Check ($parsedMarker.PitCrewSha -ceq $pitCrewSha) (
    'Release marker did not preserve the PitCrew SHA.')
Add-Check (
    ($parsedMarker.TopologyProfiles -join ',') -ceq
        'portable,containerized,windows-installed,linux-installed'
) 'Release marker did not preserve all required topology profiles.'
Add-Check ($parsedMarker.RunId -eq $runId) (
    'Release marker did not preserve the workflow run ID.')

Add-RejectionCheck 'Missing containerized profile marker' {
    Read-SupportReleaseGateMarker -ReleaseBody (
        $releaseBody.Replace(
            'portable,containerized,windows-installed,linux-installed',
            'portable,windows-installed,linux-installed'))
}
Add-RejectionCheck 'Missing Linux-installed profile marker' {
    Read-SupportReleaseGateMarker -ReleaseBody (
        $releaseBody.Replace(
            'portable,containerized,windows-installed,linux-installed',
            'portable,containerized,windows-installed'))
}
Add-RejectionCheck 'Duplicate containerized profile marker' {
    Read-SupportReleaseGateMarker -ReleaseBody (
        $releaseBody.Replace(
            'portable,containerized,windows-installed,linux-installed',
            'portable,containerized,containerized,windows-installed,linux-installed'))
}
Add-RejectionCheck 'Reordered topology profile marker' {
    Read-SupportReleaseGateMarker -ReleaseBody (
        $releaseBody.Replace(
            'portable,containerized,windows-installed,linux-installed',
            'containerized,portable,windows-installed,linux-installed'))
}

$verified = Assert-SupportReleaseGateEvidence `
    -Release (New-ReleaseFixture) `
    -WorkflowRun (New-WorkflowRunFixture) `
    -WorkflowJobs (New-WorkflowJobsFixture) `
    -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
    -ExpectedReleaseSha $dashboardSha `
    -PolicyPath $policyPath
Add-Check ($verified.RunId -eq $runId) (
    'Valid release gate evidence did not return the verified marker.')

$prereleaseMarker = New-SupportReleaseGateMarker `
    -ReleaseTag 'v1.2.3-beta.1' `
    -DashboardSha $dashboardSha `
    -PitCrewSha $pitCrewSha `
    -RunId $runId
$verifiedPrerelease = Assert-SupportReleaseGateEvidence `
    -Release (
        New-ReleaseFixture `
            -Tag 'v1.2.3-beta.1' `
            -Prerelease $true `
            -Body $prereleaseMarker) `
    -WorkflowRun (
        New-WorkflowRunFixture -Tag 'v1.2.3-beta.1') `
    -WorkflowJobs (New-WorkflowJobsFixture) `
    -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
    -ExpectedReleaseSha $dashboardSha `
    -PolicyPath $policyPath
Add-Check ($verifiedPrerelease.ReleaseTag -ceq 'v1.2.3-beta.1') (
    'Valid prerelease gate evidence was not accepted.')

Add-RejectionCheck 'Missing gate marker' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture -Body 'Generated notes only.') `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Duplicate gate marker' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture -Body "$marker`n$marker") `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Human-created release' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture -Author 'maintainer') `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Draft release' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture -Draft $true) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Release target drift' {
    Assert-SupportReleaseGateEvidence `
        -Release (
            New-ReleaseFixture `
                -Target 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb') `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Release tag drift' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture -Tag 'v1.2.4') `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Release prerelease drift' {
    Assert-SupportReleaseGateEvidence `
        -Release (
            New-ReleaseFixture -Tag 'v1.2.3-beta.1') `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Failed gate run' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (
            New-WorkflowRunFixture -Conclusion 'failure') `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Wrong gate workflow' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (
            New-WorkflowRunFixture `
                -Path '.github/workflows/support-canary.yml') `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Wrong gate run name' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (
            New-WorkflowRunFixture -Name 'Prepare gated release') `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Wrong gate repository' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (
            New-WorkflowRunFixture -Repository 'other/repository') `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Wrong gate branch' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture -Branch 'feature') `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Wrong gate SHA' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (
            New-WorkflowRunFixture `
                -Sha 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb') `
        -WorkflowJobs (New-WorkflowJobsFixture) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Uppercase marker SHA' {
    New-SupportReleaseGateMarker `
        -ReleaseTag 'v1.2.3' `
        -DashboardSha $dashboardSha.ToUpperInvariant() `
        -PitCrewSha $pitCrewSha `
        -RunId $runId
}
Add-RejectionCheck 'Zero marker run ID' {
    New-SupportReleaseGateMarker `
        -ReleaseTag 'v1.2.3' `
        -DashboardSha $dashboardSha `
        -PitCrewSha $pitCrewSha `
        -RunId 0
}
Add-RejectionCheck 'Invalid marker release tag' {
    New-SupportReleaseGateMarker `
        -ReleaseTag '1.2.3' `
        -DashboardSha $dashboardSha `
        -PitCrewSha $pitCrewSha `
        -RunId $runId
}
Add-RejectionCheck 'Validate-only gate run' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (
            New-WorkflowJobsFixture -Name 'Gate release candidate') `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Missing Windows-installed gate job' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (
            New-WorkflowJobsFixture `
                -Name @(
                    'Gate release candidate / Portable support canary'
                    'Gate containerized candidate / Containerized support canary'
                    'Gate installed Linux candidate / Linux-installed support canary'
                    'Create gated draft release'
                )) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Missing containerized gate job' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (
            New-WorkflowJobsFixture `
                -Name @(
                    'Gate release candidate / Portable support canary'
                    'Gate installed Windows candidate / Windows-installed support canary'
                    'Gate installed Linux candidate / Linux-installed support canary'
                    'Create gated draft release'
                )) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Missing Linux-installed gate job' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (
            New-WorkflowJobsFixture `
                -Name @(
                    'Gate release candidate / Portable support canary'
                    'Gate containerized candidate / Containerized support canary'
                    'Gate installed Windows candidate / Windows-installed support canary'
                    'Create gated draft release'
                )) `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Failed containerized gate job' {
    $workflowJobs = New-WorkflowJobsFixture
    $containerJob = @(
        $workflowJobs.jobs | Where-Object {
            [string]$_.name -ceq
                'Gate containerized candidate / Containerized support canary'
        }
    )
    $containerJob[0].conclusion = 'failure'
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs $workflowJobs `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}
Add-RejectionCheck 'Failed draft-creation job' {
    Assert-SupportReleaseGateEvidence `
        -Release (New-ReleaseFixture) `
        -WorkflowRun (New-WorkflowRunFixture) `
        -WorkflowJobs (
            New-WorkflowJobsFixture -Conclusion 'failure') `
        -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
        -ExpectedReleaseSha $dashboardSha `
        -PolicyPath $policyPath
}

$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "pitcrew-support-release-gate-$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    $mismatchedPolicyPath = Join-Path $temporaryRoot 'policy.json'
    @{
        pitCrewCommit = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath $mismatchedPolicyPath -Encoding utf8

    Add-RejectionCheck 'Policy PitCrew mismatch' {
        Assert-SupportReleaseGateEvidence `
            -Release (New-ReleaseFixture) `
            -WorkflowRun (New-WorkflowRunFixture) `
            -WorkflowJobs (New-WorkflowJobsFixture) `
            -ExpectedRepository 'ncosentino/pitcrew-dashboard' `
            -ExpectedReleaseSha $dashboardSha `
            -PolicyPath $mismatchedPolicyPath
    }
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $modulePath,
    [ref]$tokens,
    [ref]$parseErrors) | Out-Null
Add-Check ($parseErrors.Count -eq 0) (
    'Support release gate module has PowerShell syntax errors.')
$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $publishedContainerVerifierPath,
    [ref]$tokens,
    [ref]$parseErrors) | Out-Null
Add-Check ($parseErrors.Count -eq 0) (
    'Published container verifier has PowerShell syntax errors.')

$prepareWorkflow = Get-Content -LiteralPath $prepareWorkflowPath -Raw
$verifyWorkflow = Get-Content -LiteralPath $verifyWorkflowPath -Raw
$supportCanaryWorkflow = Get-Content `
    -LiteralPath $supportCanaryWorkflowPath `
    -Raw
$publishedVerificationWorkflow = Get-Content `
    -LiteralPath $publishedVerificationWorkflowPath `
    -Raw
$publishedContainerVerifier = Get-Content `
    -LiteralPath $publishedContainerVerifierPath `
    -Raw
$ciWorkflow = Get-Content -LiteralPath $ciWorkflowPath -Raw
$supportRelayScenario = Get-Content `
    -LiteralPath $supportRelayScenarioPath `
    -Raw
Add-Check (
    $prepareWorkflow -match '(?m)^  workflow_dispatch:\r?$' -and
    $prepareWorkflow -notmatch '(?m)^  release:\r?$'
) 'Gated release preparation is not manual-only.'
Add-Check (
    $prepareWorkflow -notmatch 'pull_request_target' -and
    $prepareWorkflow -notmatch 'self-hosted' -and
    $verifyWorkflow -notmatch 'self-hosted' -and
    $supportCanaryWorkflow -notmatch 'self-hosted' -and
    $supportCanaryWorkflow -match
        '(?ms)^  windows-installed:.*?runs-on: windows-latest' -and
    $supportCanaryWorkflow -match
        '(?ms)^  containerized:.*?runs-on: ubuntu-latest' -and
    $supportCanaryWorkflow -match
        '(?ms)^  linux-installed:.*?runs-on: ubuntu-latest'
) 'Release gating crosses the public hosted-runner trust boundary.'
Add-Check (
    $supportCanaryWorkflow -match
        "(?ms)topology_profile:.*?options:.*?- containerized" -and
    $supportCanaryWorkflow -match
        '(?ms)^  containerized:.*?-TopologyProfile containerized' -and
    $supportCanaryWorkflow -match
        'support-canary-container-\$\{\{ github\.run_id \}\}'
) 'The containerized canary is not independently invocable on public infrastructure.'
Add-Check (
    $supportCanaryWorkflow -match
        "(?ms)topology_profile:.*?options:.*?- linux-installed" -and
    $supportCanaryWorkflow -match
        '(?ms)^  linux-installed:.*?-TopologyProfile linux-installed' -and
    $supportCanaryWorkflow -match
        'support-canary-linux-\$\{\{ github\.run_id \}\}'
) 'The Linux-installed canary is not independently invocable on public infrastructure.'
Add-Check (
    @(
        $canaryTopologyEntryPaths |
            Where-Object {
                (Get-Content -LiteralPath $_ -Raw) -notmatch
                    "'linux-installed'"
            }
    ).Count -eq 0
) 'A canary topology entry script rejects the Linux-installed profile.'
Add-Check (
    (Get-Content -LiteralPath $canaryBuildPath -Raw) -match
        "(?ms)topologyProfile -ceq 'linux-installed'.*?RuntimeIdentifiers'.*?'linux-x64'"
) 'The Linux-installed canary does not package the candidate Linux artifacts.'
Add-Check (
    $supportCanaryWorkflow -match
        "(?ms)scenario:.*?options:.*?- support-relay-restart-recovery-v1"
) 'The relay-restart scenario is not independently selectable.'
Add-Check (
    $supportCanaryWorkflow -match
        "(?ms)scenario:.*?options:.*?- support-diagnostic-mode-matrix-v1"
) 'The diagnostic-mode matrix scenario is not independently selectable.'
Add-Check (
    $supportCanaryWorkflow -match
        "(?ms)scenario:.*?options:.*?- support-request-rejection-matrix-v1"
) 'The request-rejection matrix scenario is not independently selectable.'
Add-Check (
    $supportCanaryWorkflow -match
        "(?ms)scenario:.*?options:.*?- support-terminal-lifecycle-v1"
) 'The terminal-lifecycle scenario is not independently selectable.'
Add-Check (
    @(
        $canaryScenarioEntryPaths |
            Where-Object {
                (Get-Content -LiteralPath $_ -Raw) -notmatch
                    "'support-diagnostic-mode-matrix-v1'"
            }
    ).Count -eq 0
) 'A canary scenario entry script rejects the diagnostic-mode matrix.'
Add-Check (
    @(
        $canaryScenarioEntryPaths |
            Where-Object {
                (Get-Content -LiteralPath $_ -Raw) -notmatch
                    "'support-request-rejection-matrix-v1'"
            }
    ).Count -eq 0
) 'A canary scenario entry script rejects the request-rejection matrix.'
Add-Check (
    @(
        $canaryScenarioEntryPaths |
            Where-Object {
                (Get-Content -LiteralPath $_ -Raw) -notmatch
                    "'support-terminal-lifecycle-v1'"
            }
    ).Count -eq 0
) 'A canary scenario entry script rejects the terminal-lifecycle scenario.'
Add-Check (
    $supportRelayScenario -match
        "(?ms)ValidateSet\(.*?'ConnectorOffline'.*?'CapacityMismatch'.*?'JobNotAssigned'.*?'HostPressure'.*?'Full'.*?\)"
) 'The support relay wrapper does not accept the complete closed diagnostic-mode set.'
Add-Check (
    $supportRelayScenario -match
        '(?m)^\s+-DiagnosticMode \$DiagnosticMode `\r?$' -and
    $supportRelayScenario -notmatch
        '(?m)^\s+-DiagnosticMode ConnectorOffline `\r?$'
) 'The support relay wrapper does not forward the selected diagnostic mode.'
Add-Check (
    $prepareWorkflow -match
        'uses: \./\.github/workflows/support-canary\.yml' -and
    ([regex]::Matches(
        $prepareWorkflow,
        'scenario: support-terminal-lifecycle-v1'
    )).Count -eq 4 -and
    $prepareWorkflow -match 'topology_profile: portable' -and
    $prepareWorkflow -match 'topology_profile: containerized' -and
    $prepareWorkflow -match 'topology_profile: windows-installed' -and
    $prepareWorkflow -match 'topology_profile: linux-installed'
) 'Release preparation does not invoke all required canary profiles.'
Add-Check (
    $prepareWorkflow -match "context\.ref !== 'refs/heads/main'" -and
    $prepareWorkflow -match
        'main\.commit\.sha !== dashboardSha' -and
    $prepareWorkflow -match
        'PitCrew input must match the candidate Dashboard evidence policy'
) 'Release preparation does not bind both immutable candidates to main and policy.'
Add-Check (
    $prepareWorkflow -match "if: inputs\.mode == 'create-draft'" -and
    $prepareWorkflow -match
        'needs: \[preflight, portable-canary, containerized-canary, windows-installed-canary, linux-installed-canary\]' -and
    $prepareWorkflow -match
        '(?ms)^  linux-installed-canary:.*?topology_profile: linux-installed' -and
    $prepareWorkflow -match 'draft: true' -and
    $prepareWorkflow -notmatch 'draft: false'
) 'Release preparation can publish or create a draft before the canary.'
Add-Check (
    $prepareWorkflow -match '(?m)^permissions: \{\}\r?$' -and
    $prepareWorkflow -match '(?m)^\s{6}contents: write\r?$' -and
    $verifyWorkflow -match '(?m)^\s{6}actions: read\r?$' -and
    $verifyWorkflow -match '(?m)^\s{6}contents: read\r?$'
) 'Release workflows do not keep permissions scoped to their exact jobs.'
Add-Check (
    $prepareWorkflow -match 'New-SupportReleaseGateMarker' -and
    $verifyWorkflow -match 'Assert-SupportReleaseGateEvidence' -and
    $verifyWorkflow -match 'actions/runs/\$\(\$marker\.RunId\)' -and
    $verifyWorkflow -match
        'actions/runs/\$\(\$marker\.RunId\)/jobs'
) 'Release preparation and verification do not share auditable gate evidence.'
Add-Check (
    $supportCanaryWorkflow -match
        "'\.github/workflows/prepare-release\.yml'" -and
    $supportCanaryWorkflow -match
        "'\.github/workflows/verify-support-release-gate\.yml'" -and
    $supportCanaryWorkflow -match
        "'\.github/workflows/verify-published-release\.yml'" -and
    $supportCanaryWorkflow -match "'scripts/release/\*\*'"
) 'Release-gate changes do not trigger the portable canary.'

$supportPublisher = Get-Content `
    -LiteralPath (
        Join-Path `
            $repositoryRoot `
            '.github/workflows/publish-support-plane.yml'
    ) `
    -Raw
$connectorPublisher = Get-Content `
    -LiteralPath (
        Join-Path `
            $repositoryRoot `
            '.github/workflows/publish-host-connector.yml'
    ) `
    -Raw
$containerPublisher = Get-Content `
    -LiteralPath (
        Join-Path `
            $repositoryRoot `
            '.github/workflows/publish-container.yml'
    ) `
    -Raw
Add-Check (
    $supportPublisher -match 'actions/upload-artifact@v6' -and
    $supportPublisher -notmatch 'actions/upload-artifact@v5' -and
    $supportPublisher -match
        'Test-PublishedReleaseAssets\.ps1'
) 'Support publication does not use supported artifacts and verify published digests.'
Add-Check (
    $connectorPublisher -match
        'Test-PublishedReleaseAssets\.ps1' -and
    $connectorPublisher -match
        'Enable-PitCrewCapacityOperations\.ps1'
) 'Connector publication does not verify archives, sidecars, and the operator installer.'
Add-Check (
    $containerPublisher -match
        '(?ms)Attest image provenance.*?Verify published image.*?Test-PublishedContainerImage\.ps1' -and
    $containerPublisher -match
        'RequireProvenanceAttestation'
) 'Container publication does not verify tags, indexes, and provenance after publishing.'
Add-Check (
    $publishedContainerVerifier.Contains(
        "--format '{{json .Manifest}}'",
        [StringComparison]::Ordinal) -and
    $publishedContainerVerifier -match
        '\$semanticDigest\s*=\s*\[string\]\$semanticIndex\.digest' -and
    $publishedContainerVerifier -match
        '\$immutableDigest\s*=\s*\[string\]\$immutableIndex\.digest' -and
    $publishedContainerVerifier -notmatch '--raw|Get-FileHash'
) 'Container verification does not derive tag digests from structured registry metadata.'
Add-Check (
    $publishedVerificationWorkflow -match
        '(?m)^  workflow_dispatch:\r?$' -and
    $publishedVerificationWorkflow -notmatch
        '(?m)^  release:\r?$' -and
    $publishedVerificationWorkflow -notmatch
        'pull_request_target|self-hosted' -and
    $publishedVerificationWorkflow -notmatch
        'ref: \$\{\{ inputs\.release_sha \}\}' -and
    $publishedVerificationWorkflow -match
        '(?m)^  packages: read\r?$' -and
    $publishedVerificationWorkflow -match
        '(?m)^  attestations: read\r?$' -and
    $publishedVerificationWorkflow -match
        '(?ms)^  preflight:.*?target_commitish.*?^  assets:.*?needs: preflight' -and
    $publishedVerificationWorkflow -match
        '(?ms)^  images:.*?needs: preflight' -and
    $publishedVerificationWorkflow -match
        'Test-PublishedReleaseAssets\.ps1' -and
    $publishedVerificationWorkflow -match
        'Test-PublishedContainerImage\.ps1'
) 'Published release verification is not a read-only complete hosted audit.'
Add-Check (
    $ciWorkflow -match
        'Test-PublishedReleaseVerification\.ps1'
) 'CI does not execute the offline published-release verification contract.'

foreach ($publishWorkflowPath in $publishWorkflowPaths) {
    $publishWorkflow = Get-Content -LiteralPath $publishWorkflowPath -Raw
    Add-Check (
        $publishWorkflow -match
            'uses: \./\.github/workflows/verify-support-release-gate\.yml' -and
        $publishWorkflow -match '(?m)^\s+needs: release-gate\s*$'
    ) (
        "$(Split-Path $publishWorkflowPath -Leaf) does not require the " +
        'verified release gate.')
}

$publishSupportWorkflow = Get-Content `
    -LiteralPath $publishWorkflowPaths[2] `
    -Raw
Add-Check (
    $publishSupportWorkflow -match
        'required: \$\{\{ github\.event_name == ''release'' \}\}' -and
    $publishSupportWorkflow -match
        "if: github\.event_name == 'release'"
) 'Manual support packaging can upload release assets without gate evidence.'

if ($errors.Count -gt 0) {
    throw (
        "Support release gate tests failed after $checks checks:`n" +
        ($errors -join "`n"))
}

Write-Host "Support release gate tests passed: $checks checks."
