#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reporterModule = Join-Path $repositoryRoot 'scripts' 'coverage' 'CoverageReport.psm1'
$commentModule = Join-Path $repositoryRoot 'scripts' 'coverage' 'CoverageComment.psm1'
$workflowPath = Join-Path $repositoryRoot '.github' 'workflows' 'ci.yml'
$buildPropsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$packagesPath = Join-Path $repositoryRoot 'Directory.Packages.props'
$toolManifestPath = Join-Path $repositoryRoot '.config' 'dotnet-tools.json'
$errors = [Collections.Generic.List[string]]::new()
$checks = 0
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "pitcrew-coverage-$([guid]::NewGuid())"

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

function New-CommentPage {
    param(
        [AllowEmptyCollection()]
        [object[]] $Nodes,

        [bool] $HasNextPage,

        [AllowNull()]
        [string] $EndCursor
    )

    return [pscustomobject]@{
        data = [pscustomobject]@{
            repository = [pscustomobject]@{
                pullRequest = [pscustomobject]@{
                    comments = [pscustomobject]@{
                        nodes = $Nodes
                        pageInfo = [pscustomobject]@{
                            hasNextPage = $HasNextPage
                            endCursor = $EndCursor
                        }
                    }
                }
            }
        }
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $coberturaPath = Join-Path $fixtureRoot 'backend.cobertura.xml'
    $frontendPath = Join-Path $fixtureRoot 'coverage-summary.json'
    $changedFilesPath = Join-Path $fixtureRoot 'changed-files.json'

    $validCobertura = @'
<?xml version="1.0" encoding="utf-8"?>
<coverage branch-rate="1">
  <packages>
    <package name="PitCrew.Sample">
      <classes>
        <class name="PitCrew.Sample.Alpha" filename="src/PitCrew.Sample/Alpha.cs">
          <methods>
            <method name="Covered">
              <lines><line number="10" hits="1" /></lines>
            </method>
            <method name="Missed">
              <lines><line number="20" hits="0" /></lines>
            </method>
          </methods>
          <lines>
            <line number="10" hits="1" branch="true" condition-coverage="100% (2/2)" />
            <line number="20" hits="0" />
            <line number="30" hits="1" branch="true" condition-coverage="50% (1/2)" />
          </lines>
        </class>
        <class name="PitCrew.Sample.Alpha.Generated" filename="src/PitCrew.Sample/Alpha.cs">
          <lines>
            <line number="10" hits="0" branch="true" condition-coverage="100% (2/2)" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
'@
    Set-Content -LiteralPath $coberturaPath -Value $validCobertura -Encoding utf8

    $validFrontend = @{
        total = @{
            lines = @{ total = 4; covered = 3; skipped = 0; pct = 75 }
            statements = @{ total = 4; covered = 3; skipped = 0; pct = 75 }
            functions = @{ total = 2; covered = 1; skipped = 0; pct = 50 }
            branches = @{ total = 2; covered = 1; skipped = 0; pct = 50 }
        }
        'C:/repo/src/PitCrew.Dashboard.WebApi/ClientApp/src/App.tsx' = @{
            lines = @{ total = 4; covered = 3; skipped = 0; pct = 75 }
            statements = @{ total = 4; covered = 3; skipped = 0; pct = 75 }
            functions = @{ total = 2; covered = 1; skipped = 0; pct = 50 }
            branches = @{ total = 2; covered = 1; skipped = 0; pct = 50 }
        }
    } | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $frontendPath -Value $validFrontend -Encoding utf8

    @(
        'src/PitCrew.Sample/Alpha.cs'
        'src/PitCrew.Dashboard.WebApi/ClientApp/src/App.tsx'
        'docs/README.md'
    ) | ConvertTo-Json |
        Set-Content -LiteralPath $changedFilesPath -Encoding utf8

    Import-Module $reporterModule -Force
    Import-Module $commentModule -Force

    $report = New-CoverageReport `
        -CoberturaPath $coberturaPath `
        -FrontendSummaryPath $frontendPath `
        -ChangedFilesPath $changedFilesPath `
        -RepositoryRoot $repositoryRoot `
        -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123456789'

    Add-Check (
        $report -match '\| C# \| 66\.67% \| 75\.00% \| 50\.00% \|'
    ) 'The merged report omitted exact C# line, deduplicated branch, or function coverage.'
    Add-Check (
        $report -notmatch '\| C# \| 66\.67% \| 100\.00% \|'
    ) 'The report trusted a top-level branch rate instead of condition totals.'
    Add-Check (
        $report -match
            'C# coverage uses condition and method totals from the merged Coverlet Cobertura report\.'
    ) 'The report did not explain the C# branch metric source accurately.'
    Add-Check (
        $report -match '\| Frontend \| 75\.00% \| 50\.00% \| 50\.00% \|'
    ) 'The report omitted exact frontend line, branch, or function coverage.'
    Add-Check (
        $report -match 'src/PitCrew\.Sample/Alpha\.cs'
    ) 'The report omitted a changed C# production file.'
    Add-Check (
        $report -match 'src/PitCrew\.Dashboard\.WebApi/ClientApp/src/App\.tsx'
    ) 'The report omitted a changed frontend production file.'
    Add-Check (
        $report -match 'https://github\.com/ncosentino/pitcrew-dashboard/actions/runs/123456789'
    ) 'The report omitted the exact workflow run link.'
    Add-Check (
        $report.Length -lt 60000
    ) 'The report exceeded the bounded workflow-output size contract.'

    $reversedCobertura = [regex]::Replace(
        $validCobertura,
        '(?s)(        <class name="PitCrew\.Sample\.Alpha".*?</class>\s*)(        <class name="PitCrew\.Sample\.Alpha\.Generated".*?</class>)',
        '$2$1'
    )
    if ($reversedCobertura -ceq $validCobertura) {
        throw 'Could not reverse the duplicate-line coverage fixture.'
    }
    Set-Content -LiteralPath $coberturaPath -Value $reversedCobertura -Encoding utf8
    $reversedReport = New-CoverageReport `
        -CoberturaPath $coberturaPath `
        -FrontendSummaryPath $frontendPath `
        -ChangedFilesPath $changedFilesPath `
        -RepositoryRoot $repositoryRoot `
        -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123456789'
    Add-Check (
        $reversedReport -match '\| C# \| 66\.67% \| 75\.00% \| 50\.00% \|'
    ) 'Duplicate C# line hit union changed when class order was reversed.'
    Set-Content -LiteralPath $coberturaPath -Value $validCobertura -Encoding utf8

    foreach ($roundedCondition in @(
            @{
                Value = '67% (4/6)'
                ExpectedBranchPercent = '75.00%'
            }
            @{
                Value = '83% (5/6)'
                ExpectedBranchPercent = '87.50%'
            }
            @{
                Value = '17% (1/6)'
                ExpectedBranchPercent = '37.50%'
            }
        )) {
        $roundedCobertura = $validCobertura.Replace(
            '50% (1/2)',
            $roundedCondition.Value,
            [StringComparison]::Ordinal
        )
        Set-Content -LiteralPath $coberturaPath -Value $roundedCobertura -Encoding utf8
        $roundedReport = New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
        Add-Check (
            $roundedReport -match
                "\| C# \| 66\.67% \| $([regex]::Escape($roundedCondition.ExpectedBranchPercent)) \|"
        ) "ReportGenerator-rounded condition coverage '$($roundedCondition.Value)' was rejected."
    }
    Set-Content -LiteralPath $coberturaPath -Value $validCobertura -Encoding utf8

    Add-RejectionCheck 'Missing changed-file data' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath (Join-Path $fixtureRoot 'missing.json') `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    Set-Content -LiteralPath $frontendPath -Value '{}' -Encoding utf8
    Add-RejectionCheck 'Malformed frontend coverage' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }
    Set-Content -LiteralPath $frontendPath -Value $validFrontend -Encoding utf8

    Add-RejectionCheck 'Missing frontend coverage' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath (Join-Path $fixtureRoot 'missing-summary.json') `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    Set-Content -LiteralPath $coberturaPath -Value '<coverage>' -Encoding utf8
    Add-RejectionCheck 'Malformed C# coverage' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }
    Set-Content -LiteralPath $coberturaPath -Value $validCobertura -Encoding utf8

    $malformedConditionCoverage = $validCobertura.Replace(
        'condition-coverage="100% (2/2)"',
        'condition-coverage="not coverage"',
        [StringComparison]::Ordinal
    )
    Set-Content -LiteralPath $coberturaPath -Value $malformedConditionCoverage -Encoding utf8
    Add-RejectionCheck 'Malformed C# condition coverage' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    $missingConditionCoverage = $validCobertura.Replace(
        ' condition-coverage="50% (1/2)"',
        '',
        [StringComparison]::Ordinal
    )
    Set-Content -LiteralPath $coberturaPath -Value $missingConditionCoverage -Encoding utf8
    Add-RejectionCheck 'Missing C# condition coverage' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    $inconsistentConditionCoverage = $validCobertura.Replace(
        'condition-coverage="100% (2/2)"',
        'condition-coverage="100% (1/2)"',
        [StringComparison]::Ordinal
    )
    Set-Content -LiteralPath $coberturaPath -Value $inconsistentConditionCoverage -Encoding utf8
    Add-RejectionCheck 'Inconsistent C# condition coverage' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    foreach ($inconsistentCondition in @(
            '66% (4/6)'
            '82% (5/6)'
            '16% (1/6)'
            '12% (1/8)'
        )) {
        $boundaryCobertura = $validCobertura.Replace(
            '50% (1/2)',
            $inconsistentCondition,
            [StringComparison]::Ordinal
        )
        Set-Content -LiteralPath $coberturaPath -Value $boundaryCobertura -Encoding utf8
        Add-RejectionCheck "Rounded boundary condition coverage $inconsistentCondition" {
            New-CoverageReport `
                -CoberturaPath $coberturaPath `
                -FrontendSummaryPath $frontendPath `
                -ChangedFilesPath $changedFilesPath `
                -RepositoryRoot $repositoryRoot `
                -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
        }
    }

    $impossibleConditionCoverage = $validCobertura.Replace(
        'condition-coverage="50% (1/2)"',
        'condition-coverage="150% (3/2)"',
        [StringComparison]::Ordinal
    )
    Set-Content -LiteralPath $coberturaPath -Value $impossibleConditionCoverage -Encoding utf8
    Add-RejectionCheck 'Impossible C# condition counts' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    $conflictingConditionTotals = $validCobertura.Replace(
        '<line number="10" hits="0" branch="true" condition-coverage="100% (2/2)" />',
        '<line number="10" hits="0" branch="true" condition-coverage="67% (2/3)" />',
        [StringComparison]::Ordinal
    )
    Set-Content -LiteralPath $coberturaPath -Value $conflictingConditionTotals -Encoding utf8
    Add-RejectionCheck 'Conflicting C# condition totals' {
        New-CoverageReport `
            -CoberturaPath $coberturaPath `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }
    Set-Content -LiteralPath $coberturaPath -Value $validCobertura -Encoding utf8

    Add-RejectionCheck 'Missing C# coverage' {
        New-CoverageReport `
            -CoberturaPath (Join-Path $fixtureRoot 'missing.cobertura.xml') `
            -FrontendSummaryPath $frontendPath `
            -ChangedFilesPath $changedFilesPath `
            -RepositoryRoot $repositoryRoot `
            -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    }

    $marker = '<!-- pitcrew-coverage-report -->'
    $newPlan = Get-CoverageCommentPlan `
        -Comments @() `
        -Report $report `
        -Marker $marker `
        -ExpectedAuthorLogin 'github-actions[bot]'
    Add-Check ($newPlan.Method -ceq 'POST') 'A missing coverage comment did not plan one POST.'
    Add-Check (
        $newPlan.Body.StartsWith($marker, [StringComparison]::Ordinal)
    ) 'The coverage comment body did not start with the deterministic marker.'

    $existingPlan = Get-CoverageCommentPlan `
        -Comments @(
            [pscustomobject]@{
                id = 42
                body = "$marker`nold"
                authorLogin = 'github-actions[bot]'
                authorType = 'Bot'
            }
            [pscustomobject]@{
                id = 43
                body = "$marker`nforeign"
                authorLogin = 'contributor'
                authorType = 'User'
            }
        ) `
        -Report $report `
        -Marker $marker `
        -ExpectedAuthorLogin 'github-actions[bot]'
    Add-Check ($existingPlan.Method -ceq 'PATCH') 'An existing coverage comment did not plan one PATCH.'
    Add-Check ($existingPlan.CommentId -eq 42) 'The upsert plan targeted the wrong comment.'
    Add-Check (
        $existingPlan.DeleteCommentIds.Count -eq 0
    ) 'A foreign marked comment was selected for deletion.'

    $foreignPlan = Get-CoverageCommentPlan `
        -Comments @(
            [pscustomobject]@{
                id = 40
                body = "$marker`nforeign"
                authorLogin = 'contributor'
                authorType = 'User'
            }
        ) `
        -Report $report `
        -Marker $marker `
        -ExpectedAuthorLogin 'github-actions[bot]'
    Add-Check (
        $foreignPlan.Method -ceq 'POST' -and $foreignPlan.DeleteCommentIds.Count -eq 0
    ) 'A foreign marked comment blocked creation of the bot-owned coverage comment.'

    $duplicatePlan = Get-CoverageCommentPlan `
        -Comments @(
            [pscustomobject]@{
                id = 43
                body = "$marker`ntwo"
                authorLogin = 'github-actions[bot]'
                authorType = 'Bot'
            }
            [pscustomobject]@{
                id = 41
                body = "$marker`nforeign"
                authorLogin = 'coverage-app[bot]'
                authorType = 'Bot'
            }
            [pscustomobject]@{
                id = 42
                body = "$marker`none"
                authorLogin = 'github-actions[bot]'
                authorType = 'Bot'
            }
        ) `
        -Report $report `
        -Marker $marker `
        -ExpectedAuthorLogin 'github-actions[bot]'
    Add-Check (
        $duplicatePlan.Method -ceq 'PATCH' -and
        $duplicatePlan.CommentId -eq 42 -and
        @($duplicatePlan.DeleteCommentIds).Count -eq 1 -and
        $duplicatePlan.DeleteCommentIds[0] -eq 43
    ) 'Bot-owned duplicate comments did not select and clean up deterministically.'

    Add-RejectionCheck 'Malformed comment data' {
        Get-CoverageCommentPlan `
            -Comments @(
                [pscustomobject]@{
                    body = "$marker`nmissing id"
                    authorLogin = 'github-actions[bot]'
                    authorType = 'Bot'
                }
            ) `
            -Report $report `
            -Marker $marker `
            -ExpectedAuthorLogin 'github-actions[bot]'
    }

    $requests = [Collections.Generic.List[object]]::new()
    $apiInvoker = {
        param([pscustomobject] $Request)
        $requests.Add($Request)
        if ($Request.Kind -ceq 'GraphQL') {
            return [pscustomobject]@{
                data = [pscustomobject]@{
                    repository = [pscustomobject]@{
                        pullRequest = [pscustomobject]@{
                            comments = [pscustomobject]@{
                                nodes = @(
                                    [pscustomobject]@{
                                        databaseId = 40
                                        body = "$marker`nforeign"
                                        author = [pscustomobject]@{
                                            login = 'contributor'
                                            __typename = 'User'
                                        }
                                    }
                                )
                                pageInfo = [pscustomobject]@{ hasNextPage = $false }
                            }
                        }
                    }
                }
            }
        }
        return $null
    }
    $published = Publish-CoverageComment `
        -Report $report `
        -Repository 'ncosentino/pitcrew-dashboard' `
        -PullRequestNumber 282 `
        -Token 'test-token' `
        -ApiInvoker $apiInvoker
    Add-Check ($published.Method -ceq 'POST') 'The tested publisher did not create a missing comment.'
    Add-Check (
        $requests.Count -eq 2
    ) 'The tested publisher did not use one bounded page and one write.'
    Add-Check (
        $requests[0].Kind -ceq 'GraphQL' -and
        $requests[0].Query -match 'author \{ login __typename \}' -and
        $requests[0].Query -match 'comments\(first: 100, after: \$cursor\)' -and
        $requests[0].Query -match 'pageInfo \{ hasNextPage endCursor \}' -and
        $requests[1].Endpoint -ceq 'repos/ncosentino/pitcrew-dashboard/issues/282/comments'
    ) 'The tested publisher used an unexpected lookup or create endpoint.'

    $updateRequests = [Collections.Generic.List[object]]::new()
    $updateInvoker = {
        param([pscustomobject] $Request)
        $updateRequests.Add($Request)
        if ($Request.Kind -ceq 'GraphQL') {
            return [pscustomobject]@{
                data = [pscustomobject]@{
                    repository = [pscustomobject]@{
                        pullRequest = [pscustomobject]@{
                            comments = [pscustomobject]@{
                                nodes = @(
                                    [pscustomobject]@{
                                        databaseId = 42
                                        body = "$marker`nold"
                                        author = [pscustomobject]@{
                                            login = 'github-actions[bot]'
                                            __typename = 'Bot'
                                        }
                                    }
                                )
                                pageInfo = [pscustomobject]@{ hasNextPage = $false }
                            }
                        }
                    }
                }
            }
        }
        return $null
    }
    $updated = Publish-CoverageComment `
        -Report $report `
        -Repository 'ncosentino/pitcrew-dashboard' `
        -PullRequestNumber 282 `
        -Token 'test-token' `
        -ApiInvoker $updateInvoker
    Add-Check (
        $updated.Method -ceq 'PATCH' -and
        $updateRequests[1].Endpoint -ceq 'repos/ncosentino/pitcrew-dashboard/issues/comments/42'
    ) 'The tested publisher did not update the deterministic existing comment.'

    $pageTwoRequests = [Collections.Generic.List[object]]::new()
    $pageTwoInvoker = {
        param([pscustomobject] $Request)
        $pageTwoRequests.Add($Request)
        if ($Request.Kind -cne 'GraphQL') {
            return $null
        }
        if ($null -eq $Request.Cursor) {
            return New-CommentPage `
                -Nodes @(
                    [pscustomobject]@{
                        databaseId = 40
                        body = "$marker`nforeign"
                        author = [pscustomobject]@{
                            login = 'contributor'
                            __typename = 'User'
                        }
                    }
                ) `
                -HasNextPage $true `
                -EndCursor 'cursor-1'
        }
        return New-CommentPage `
            -Nodes @(
                [pscustomobject]@{
                    databaseId = 42
                    body = "$marker`nbot"
                    author = [pscustomobject]@{
                        login = 'github-actions[bot]'
                        __typename = 'Bot'
                    }
                }
            ) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $pageTwoPlan = Publish-CoverageComment `
        -Report $report `
        -Repository 'ncosentino/pitcrew-dashboard' `
        -PullRequestNumber 282 `
        -Token 'test-token' `
        -ApiInvoker $pageTwoInvoker
    Add-Check (
        $pageTwoPlan.Method -ceq 'PATCH' -and
        $pageTwoPlan.CommentId -eq 42 -and
        $pageTwoRequests.Count -eq 3 -and
        $pageTwoRequests[1].Cursor -ceq 'cursor-1'
    ) 'The publisher did not find the bot-owned marker on the second bounded page.'

    $foreignPageRequests = [Collections.Generic.List[object]]::new()
    $foreignPageInvoker = {
        param([pscustomobject] $Request)
        $foreignPageRequests.Add($Request)
        if ($Request.Kind -cne 'GraphQL') {
            return $null
        }
        $isFirstPage = $null -eq $Request.Cursor
        $login = if ($isFirstPage) { 'first-contributor' } else { 'second-app[bot]' }
        $databaseId = if ($isFirstPage) { 40 } else { 41 }
        $authorType = if ($isFirstPage) { 'User' } else { 'Bot' }
        $endCursor = if ($isFirstPage) { 'cursor-1' } else { $null }
        return New-CommentPage `
            -Nodes @(
                [pscustomobject]@{
                    databaseId = $databaseId
                    body = "$marker`nforeign"
                    author = [pscustomobject]@{
                        login = $login
                        __typename = $authorType
                    }
                }
            ) `
            -HasNextPage $isFirstPage `
            -EndCursor $endCursor
    }
    $foreignPagePlan = Publish-CoverageComment `
        -Report $report `
        -Repository 'ncosentino/pitcrew-dashboard' `
        -PullRequestNumber 282 `
        -Token 'test-token' `
        -ApiInvoker $foreignPageInvoker
    Add-Check (
        $foreignPagePlan.Method -ceq 'POST' -and
        $foreignPageRequests.Count -eq 3 -and
        $foreignPageRequests[2].Endpoint -ceq
            'repos/ncosentino/pitcrew-dashboard/issues/282/comments'
    ) 'Foreign markers across pages blocked creation of the bot-owned comment.'

    $cleanupRequests = [Collections.Generic.List[object]]::new()
    $cleanupInvoker = {
        param([pscustomobject] $Request)
        $cleanupRequests.Add($Request)
        if ($Request.Kind -cne 'GraphQL') {
            return $null
        }
        if ($null -eq $Request.Cursor) {
            return New-CommentPage `
                -Nodes @(
                    [pscustomobject]@{
                        databaseId = 43
                        body = "$marker`nduplicate"
                        author = [pscustomobject]@{
                            login = 'github-actions[bot]'
                            __typename = 'Bot'
                        }
                    }
                    [pscustomobject]@{
                        databaseId = 41
                        body = "$marker`nforeign"
                        author = [pscustomobject]@{
                            login = 'contributor'
                            __typename = 'User'
                        }
                    }
                ) `
                -HasNextPage $true `
                -EndCursor 'cursor-1'
        }
        return New-CommentPage `
            -Nodes @(
                [pscustomobject]@{
                    databaseId = 42
                    body = "$marker`ncanonical"
                    author = [pscustomobject]@{
                        login = 'github-actions[bot]'
                        __typename = 'Bot'
                    }
                }
            ) `
            -HasNextPage $false `
            -EndCursor $null
    }
    [void](Publish-CoverageComment `
        -Report $report `
        -Repository 'ncosentino/pitcrew-dashboard' `
        -PullRequestNumber 282 `
        -Token 'test-token' `
        -ApiInvoker $cleanupInvoker)
    Add-Check (
        $cleanupRequests.Count -eq 4 -and
        $cleanupRequests[2].Method -ceq 'DELETE' -and
        $cleanupRequests[2].Endpoint -ceq 'repos/ncosentino/pitcrew-dashboard/issues/comments/43' -and
        $cleanupRequests[3].Method -ceq 'PATCH' -and
        $cleanupRequests[3].Endpoint -ceq 'repos/ncosentino/pitcrew-dashboard/issues/comments/42'
    ) 'The publisher did not clean up bot-owned duplicates across bounded pages.'

    $nonProgressRequests = [Collections.Generic.List[object]]::new()
    $nonProgressInvoker = {
        param([pscustomobject] $Request)
        $nonProgressRequests.Add($Request)
        return New-CommentPage `
            -Nodes @() `
            -HasNextPage $true `
            -EndCursor 'cursor-1'
    }
    Add-RejectionCheck 'Non-progressing comment cursor' {
        Publish-CoverageComment `
            -Report $report `
            -Repository 'ncosentino/pitcrew-dashboard' `
            -PullRequestNumber 282 `
            -Token 'test-token' `
            -ApiInvoker $nonProgressInvoker
    }
    Add-Check (
        $nonProgressRequests.Count -eq 2
    ) 'Cursor non-progression was not rejected on the repeated cursor page.'

    $capNodes = @(
        for ($index = 1; $index -le 100; $index++) {
            [pscustomobject]@{
                databaseId = $index
                body = 'unrelated'
                author = [pscustomobject]@{
                    login = "contributor-$index"
                    __typename = 'User'
                }
            }
        }
    )
    $capRequests = [Collections.Generic.List[object]]::new()
    $capInvoker = {
        param([pscustomobject] $Request)
        $capRequests.Add($Request)
        if ($null -eq $Request.Cursor) {
            return New-CommentPage `
                -Nodes $capNodes `
                -HasNextPage $true `
                -EndCursor 'cursor-1'
        }
        return New-CommentPage `
            -Nodes @(
                [pscustomobject]@{
                    databaseId = 101
                    body = 'unrelated'
                    author = [pscustomobject]@{
                        login = 'contributor-101'
                        __typename = 'User'
                    }
                }
            ) `
            -HasNextPage $false `
            -EndCursor $null
    }
    Add-RejectionCheck 'Comment pagination safety cap' {
        Publish-CoverageComment `
            -Report $report `
            -Repository 'ncosentino/pitcrew-dashboard' `
            -PullRequestNumber 282 `
            -Token 'test-token' `
            -CommentSafetyCap 100 `
            -ApiInvoker $capInvoker
    }
    Add-Check (
        $capRequests.Count -eq 2
    ) 'The comment safety cap did not stop pagination after the first excess entry.'

    $workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding utf8
    Add-Check (
        $workflow -notmatch 'upload_coverage|coverage-batch-|Upload coverage'
    ) 'The workflow retained the coverage artifact input or upload step.'
    Add-Check (
        $workflow -match '(?m)^  coverage:$' -and
        $workflow -match '(?m)^  coverage-comment:$'
    ) 'The workflow omitted dedicated measuring or comment jobs.'
    Add-Check (
        $workflow -match 'github\.event\.pull_request\.head\.repo\.full_name == github\.repository'
    ) 'The comment job was not restricted to same-repository pull requests.'
    Add-Check (
        $workflow -match 'pull-requests: write'
    ) 'The isolated comment job lacks the required pull-request permission.'
    $commentJob = [regex]::Match(
        $workflow,
        '(?ms)^  coverage-comment:.*?(?=^  [a-z][a-z0-9-]*:|\z)'
    ).Value
    Add-Check (
        $commentJob -match 'sparse-checkout: scripts/coverage' -and
        $commentJob -match 'persist-credentials: false'
    ) 'The privileged comment job lacks credential-free sparse checkout.'
    Add-Check (
        $commentJob -match 'Publish-CoverageComment\.ps1' -and
        $commentJob -notmatch 'actions/github-script'
    ) 'The workflow does not invoke the tested first-party comment publisher.'
    Add-Check (
        $workflow -match 'https://github\.com/\$\{\{ github\.repository \}\}/actions/runs/\$\{\{ github\.run_id \}\}'
    ) 'The workflow does not construct the exact workflow-run link.'
    Add-Check (
        $workflow -match 'Test-CoverageReporting\.ps1'
    ) 'Full CI does not run the coverage reporter/upsert contract tests.'
    Add-Check (
        $workflow -match '\$projects\.Count -ne 11' -and
        $workflow -match '--results-directory \$resultsDirectory' -and
        $workflow -match '--coverlet-file-prefix \$filePrefix' -and
        $workflow -match '\$projectReports\.Count -ne 1'
    ) 'C# coverage does not verify one unique result for all 11 test projects.'
    Add-Check (
        $workflow -match '--coverlet-output-format cobertura' -and
        $workflow -match '--coverlet-output-format opencover' -and
        $workflow -match "--coverlet-exclude '\[\*\.Tests\]\*'" -and
        $workflow -match '--coverlet-exclude-by-attribute GeneratedCodeAttribute' -and
        $workflow -match "--coverlet-exclude-by-file '\*\*/\*\.generated\.cs'"
    ) 'C# coverage lacks lossless merge, Cobertura, test-assembly, or generated-code settings.'
    Add-Check (
        $workflow -match 'dotnet tool run reportgenerator --' -and
        $workflow -match 'merged\.cobertura\.xml' -and
        $workflow -match '-CoberturaPath \$cobertura'
    ) 'The workflow does not merge per-project Coverlet reports before summarizing.'
    Add-Check (
        $workflow -match 'Test-CoverageMerge\.ps1' -and
        $workflow -notmatch 'Test-CoverageNativeMerge|dotnet-coverage|--coverage-output'
    ) 'Full coverage CI retained the obsolete native merge path.'

    [xml]$packages = Get-Content -LiteralPath $packagesPath -Raw -Encoding utf8
    $coveragePackage = @(
        $packages.Project.ItemGroup.PackageVersion |
            Where-Object Include -ceq 'coverlet.MTP'
    )
    Add-Check (
        $coveragePackage.Count -eq 1 -and
        $coveragePackage[0].Version -ceq '10.0.1'
    ) 'coverlet.MTP is not pinned to version 10.0.1.'
    $buildProps = Get-Content -LiteralPath $buildPropsPath -Raw -Encoding utf8
    Add-Check (
        $buildProps -match '<PackageReference Include="coverlet\.MTP" />' -and
        $buildProps -notmatch 'Microsoft\.Testing\.Extensions\.CodeCoverage'
    ) 'Test projects do not receive coverlet.MTP from Directory.Build.props.'

    $toolManifest = Get-Content -LiteralPath $toolManifestPath -Raw -Encoding utf8 |
        ConvertFrom-Json -Depth 10
    Add-Check (
        $toolManifest.tools.'dotnet-reportgenerator-globaltool'.version -ceq '5.5.11' -and
        $null -eq $toolManifest.tools.PSObject.Properties['dotnet-coverage']
    ) 'The local ReportGenerator tool is not pinned to 5.5.11.'
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

if ($errors.Count -gt 0) {
    throw "Coverage reporting tests failed after $checks checks:`n$($errors -join "`n")"
}

Write-Host "Coverage reporting: $checks checks passed."
