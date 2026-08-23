Set-StrictMode -Version Latest

$script:ReleaseGateMarkerPattern = (
    '(?m)^<!-- pitcrew-support-canary-gate:v1 ' +
    'release-tag=(?<releaseTag>v[0-9A-Za-z.-]+) ' +
    'dashboard-sha=(?<dashboardSha>[a-f0-9]{40}) ' +
    'pitcrew-sha=(?<pitcrewSha>[a-f0-9]{40}) ' +
    'scenario=(?<scenario>[a-z0-9-]+) ' +
    'topology-profile=(?<topologyProfile>[a-z0-9-]+) ' +
    'run-id=(?<runId>[1-9][0-9]*) -->\r?$'
)
$script:RequiredScenario = 'support-fresh-enrollment-diagnostic-v1'
$script:RequiredTopologyProfile = 'portable'
$script:RequiredWorkflowName = 'Prepare gated release'
$script:RequiredWorkflowPath = '.github/workflows/prepare-release.yml'
$script:CanonicalReleaseTagPattern = (
    '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)' +
    '(?:-(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)' +
    '(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
)

function Assert-CanonicalCommitSha {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if ($Value -cnotmatch '^[a-f0-9]{40}$') {
        throw "$Name must be a full lowercase commit SHA."
    }
}

function Assert-CanonicalReleaseTag {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    if ($Value -cnotmatch $script:CanonicalReleaseTagPattern) {
        throw 'Release tag is not a canonical semantic version.'
    }
}

function Get-RequiredPropertyValue {
    param(
        [Parameter(Mandatory)]
        [object] $InputObject,

        [Parameter(Mandatory)]
        [string] $PropertyPath,

        [Parameter(Mandatory)]
        [string] $EvidenceName
    )

    $value = $InputObject
    foreach ($segment in $PropertyPath.Split('.')) {
        if ($null -eq $value) {
            throw "$EvidenceName is missing '$PropertyPath'."
        }

        $property = $value.PSObject.Properties[$segment]
        if ($null -eq $property) {
            throw "$EvidenceName is missing '$PropertyPath'."
        }

        $value = $property.Value
    }

    if ($null -eq $value) {
        throw "$EvidenceName is missing '$PropertyPath'."
    }

    return $value
}

function New-SupportReleaseGateMarker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ReleaseTag,

        [Parameter(Mandatory)]
        [string] $DashboardSha,

        [Parameter(Mandatory)]
        [string] $PitCrewSha,

        [Parameter(Mandatory)]
        [long] $RunId
    )

    Assert-CanonicalReleaseTag -Value $ReleaseTag
    Assert-CanonicalCommitSha -Value $DashboardSha -Name 'Dashboard SHA'
    Assert-CanonicalCommitSha -Value $PitCrewSha -Name 'PitCrew SHA'
    if ($RunId -lt 1) {
        throw 'Workflow run ID must be a positive integer.'
    }

    return (
        '<!-- pitcrew-support-canary-gate:v1 ' +
        "release-tag=$ReleaseTag " +
        "dashboard-sha=$DashboardSha " +
        "pitcrew-sha=$PitCrewSha " +
        "scenario=$script:RequiredScenario " +
        "topology-profile=$script:RequiredTopologyProfile " +
        "run-id=$RunId -->"
    )
}

function Read-SupportReleaseGateMarker {
    [CmdletBinding()]
    param(
        [AllowEmptyString()]
        [string] $ReleaseBody
    )

    if ($null -eq $ReleaseBody) {
        $ReleaseBody = ''
    }

    $matches = [regex]::Matches(
        $ReleaseBody,
        $script:ReleaseGateMarkerPattern,
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($matches.Count -ne 1) {
        throw 'Release notes must contain exactly one valid support canary gate marker.'
    }

    $match = $matches[0]
    $runId = [long]::Parse(
        $match.Groups['runId'].Value,
        [Globalization.CultureInfo]::InvariantCulture)
    $marker = [pscustomobject]@{
        ReleaseTag = $match.Groups['releaseTag'].Value
        DashboardSha = $match.Groups['dashboardSha'].Value
        PitCrewSha = $match.Groups['pitcrewSha'].Value
        Scenario = $match.Groups['scenario'].Value
        TopologyProfile = $match.Groups['topologyProfile'].Value
        RunId = $runId
    }

    Assert-CanonicalReleaseTag -Value $marker.ReleaseTag
    if ($marker.Scenario -cne $script:RequiredScenario) {
        throw "Release gate scenario must be '$script:RequiredScenario'."
    }
    if ($marker.TopologyProfile -cne $script:RequiredTopologyProfile) {
        throw "Release gate topology profile must be '$script:RequiredTopologyProfile'."
    }

    return $marker
}

function Assert-SupportReleaseGateEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Release,

        [Parameter(Mandatory)]
        [object] $WorkflowRun,

        [Parameter(Mandatory)]
        [object] $WorkflowJobs,

        [Parameter(Mandatory)]
        [string] $ExpectedRepository,

        [Parameter(Mandatory)]
        [string] $ExpectedReleaseSha,

        [Parameter(Mandatory)]
        [string] $PolicyPath
    )

    Assert-CanonicalCommitSha -Value $ExpectedReleaseSha -Name 'Release SHA'
    if ($ExpectedRepository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw 'Expected repository must be an owner/repository value.'
    }
    if (-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
        throw 'Support evidence policy is unavailable.'
    }

    $releaseDraftValue = Get-RequiredPropertyValue `
        -InputObject $Release `
        -PropertyPath 'draft' `
        -EvidenceName 'Release evidence'
    if ($releaseDraftValue -isnot [bool]) {
        throw 'Release draft state must be boolean.'
    }
    if ($releaseDraftValue) {
        throw 'Release evidence must describe a published release.'
    }

    $releaseAuthor = [string](Get-RequiredPropertyValue `
        -InputObject $Release `
        -PropertyPath 'author.login' `
        -EvidenceName 'Release evidence')
    if ($releaseAuthor -cne 'github-actions[bot]') {
        throw 'Release must originate from the gated release workflow.'
    }

    $releaseTag = [string](Get-RequiredPropertyValue `
        -InputObject $Release `
        -PropertyPath 'tag_name' `
        -EvidenceName 'Release evidence')
    Assert-CanonicalReleaseTag -Value $releaseTag

    $releasePrereleaseValue = Get-RequiredPropertyValue `
        -InputObject $Release `
        -PropertyPath 'prerelease' `
        -EvidenceName 'Release evidence'
    if ($releasePrereleaseValue -isnot [bool]) {
        throw 'Release prerelease state must be boolean.'
    }
    $expectedPrerelease = $releaseTag.Contains('-')
    if ($releasePrereleaseValue -ne $expectedPrerelease) {
        throw 'Release prerelease state does not match its semantic version tag.'
    }

    $releaseTarget = [string](Get-RequiredPropertyValue `
        -InputObject $Release `
        -PropertyPath 'target_commitish' `
        -EvidenceName 'Release evidence')
    if ($releaseTarget -cne $ExpectedReleaseSha) {
        throw 'Release target does not match the immutable release SHA.'
    }

    $releaseBody = [string](Get-RequiredPropertyValue `
        -InputObject $Release `
        -PropertyPath 'body' `
        -EvidenceName 'Release evidence')
    $marker = Read-SupportReleaseGateMarker -ReleaseBody $releaseBody
    if ($marker.ReleaseTag -cne $releaseTag) {
        throw 'Release gate tag does not match the published release tag.'
    }
    if ($marker.DashboardSha -cne $ExpectedReleaseSha) {
        throw 'Release gate Dashboard SHA does not match the release SHA.'
    }

    try {
        $policy = Get-Content -LiteralPath $PolicyPath -Raw -Encoding utf8 |
            ConvertFrom-Json -Depth 20
    } catch {
        throw 'Support evidence policy is not valid JSON.'
    }

    $policyPitCrewSha = [string](Get-RequiredPropertyValue `
        -InputObject $policy `
        -PropertyPath 'pitCrewCommit' `
        -EvidenceName 'Support evidence policy')
    Assert-CanonicalCommitSha -Value $policyPitCrewSha -Name 'Policy PitCrew SHA'
    if ($marker.PitCrewSha -cne $policyPitCrewSha) {
        throw 'Release gate PitCrew SHA does not match the candidate policy.'
    }

    $runId = [long](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'id' `
        -EvidenceName 'Workflow run evidence')
    if ($runId -ne $marker.RunId) {
        throw 'Workflow run ID does not match the release gate marker.'
    }

    $runRepository = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'repository.full_name' `
        -EvidenceName 'Workflow run evidence')
    if ($runRepository -cne $ExpectedRepository) {
        throw 'Workflow run repository does not match the release repository.'
    }

    $runName = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'name' `
        -EvidenceName 'Workflow run evidence')
    if ($runName -cne $script:RequiredWorkflowName) {
        throw 'Workflow run does not belong to the gated release workflow.'
    }

    $runPath = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'path' `
        -EvidenceName 'Workflow run evidence')
    if ($runPath -cne $script:RequiredWorkflowPath) {
        throw 'Workflow run path does not match the gated release workflow.'
    }

    $runEvent = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'event' `
        -EvidenceName 'Workflow run evidence')
    if ($runEvent -cne 'workflow_dispatch') {
        throw 'Release gate workflow must be manually dispatched.'
    }

    $runBranch = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'head_branch' `
        -EvidenceName 'Workflow run evidence')
    if ($runBranch -cne 'main') {
        throw 'Release gate workflow must run from main.'
    }

    $runSha = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'head_sha' `
        -EvidenceName 'Workflow run evidence')
    if ($runSha -cne $ExpectedReleaseSha) {
        throw 'Release gate workflow SHA does not match the release SHA.'
    }

    $runStatus = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'status' `
        -EvidenceName 'Workflow run evidence')
    $runConclusion = [string](Get-RequiredPropertyValue `
        -InputObject $WorkflowRun `
        -PropertyPath 'conclusion' `
        -EvidenceName 'Workflow run evidence')
    if ($runStatus -cne 'completed' -or $runConclusion -cne 'success') {
        throw 'Release gate workflow did not complete successfully.'
    }

    $jobs = @(Get-RequiredPropertyValue `
        -InputObject $WorkflowJobs `
        -PropertyPath 'jobs' `
        -EvidenceName 'Workflow job evidence')
    $draftJobs = @(
        $jobs |
            Where-Object {
                [string]$_.name -ceq 'Create gated draft release'
            }
    )
    if ($draftJobs.Count -ne 1) {
        throw 'Release gate workflow must contain one draft-creation job.'
    }
    if (
        [string]$draftJobs[0].status -cne 'completed' -or
        [string]$draftJobs[0].conclusion -cne 'success'
    ) {
        throw 'Release gate draft-creation job did not complete successfully.'
    }

    return $marker
}

Export-ModuleMember -Function @(
    'New-SupportReleaseGateMarker',
    'Read-SupportReleaseGateMarker',
    'Assert-SupportReleaseGateEvidence'
)
