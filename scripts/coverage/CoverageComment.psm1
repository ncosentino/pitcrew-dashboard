Set-StrictMode -Version Latest

function Get-OwnedCoverageComments {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Comments,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Marker,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $ExpectedAuthorLogin
    )

    $matches = [Collections.Generic.List[object]]::new()
    foreach ($comment in $Comments) {
        $idProperty = $comment.PSObject.Properties['id']
        $bodyProperty = $comment.PSObject.Properties['body']
        if (
            $null -eq $idProperty -or
            $null -eq $bodyProperty -or
            $idProperty.Value -isnot [ValueType] -or
            $bodyProperty.Value -isnot [string]
        ) {
            throw 'GitHub returned malformed pull-request comment data.'
        }

        $authorLoginProperty = $comment.PSObject.Properties['authorLogin']
        $authorTypeProperty = $comment.PSObject.Properties['authorType']
        $isExpectedAuthor = (
            $null -ne $authorLoginProperty -and
            $null -ne $authorTypeProperty -and
            $authorLoginProperty.Value -is [string] -and
            $authorTypeProperty.Value -is [string] -and
            $authorLoginProperty.Value.Equals(
                $ExpectedAuthorLogin,
                [StringComparison]::OrdinalIgnoreCase
            ) -and
            $authorTypeProperty.Value -in @('Bot', 'App')
        )
        if (
            $isExpectedAuthor -and
            $bodyProperty.Value.StartsWith($Marker, [StringComparison]::Ordinal)
        ) {
            $matches.Add($comment)
        }
    }

    return @($matches)
}

function Get-CoverageCommentPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Comments,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Report,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Marker,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $ExpectedAuthorLogin
    )

    $matches = @(
        Get-OwnedCoverageComments `
            -Comments $Comments `
            -Marker $Marker `
            -ExpectedAuthorLogin $ExpectedAuthorLogin
    )
    $body = "$Marker`n$Report"
    if ($matches.Count -eq 0) {
        return [pscustomobject]@{
            Method = 'POST'
            CommentId = $null
            DeleteCommentIds = @()
            Body = $body
        }
    }

    $orderedMatches = @($matches | Sort-Object id)
    return [pscustomobject]@{
        Method = 'PATCH'
        CommentId = [long]$orderedMatches[0].id
        DeleteCommentIds = @(
            $orderedMatches |
                Select-Object -Skip 1 |
                ForEach-Object { [long]$_.id }
        )
        Body = $body
    }
}

function Invoke-CoverageGitHubApi {
    param([Parameter(Mandatory)][pscustomobject] $Request)

    if ($Request.Kind -ceq 'GraphQL') {
        $arguments = @(
            'api'
            'graphql'
            '-f'
            "query=$($Request.Query)"
            '-F'
            "owner=$($Request.Owner)"
            '-F'
            "name=$($Request.Name)"
            '-F'
            "number=$($Request.Number)"
        )
        if ($null -ne $Request.Cursor) {
            $arguments += @('-f', "cursor=$($Request.Cursor)")
        }
        $output = & gh @arguments
    } elseif ($Request.Kind -ceq 'REST') {
        if ($Request.Method -ceq 'DELETE') {
            $output = & gh api $Request.Endpoint --method DELETE
        } else {
            $payload = @{ body = $Request.Body } | ConvertTo-Json -Compress
            $output = $payload |
                & gh api $Request.Endpoint --method $Request.Method --input -
        }
    } else {
        throw "Unsupported GitHub API request kind '$($Request.Kind)'."
    }

    if ($LASTEXITCODE -ne 0) {
        throw "GitHub API request failed with exit code $LASTEXITCODE."
    }

    if ($Request.Kind -ceq 'REST') {
        return $null
    }

    try {
        return $output | ConvertFrom-Json -Depth 20
    } catch {
        throw "GitHub API returned malformed JSON: $($_.Exception.Message)"
    }
}

function Publish-CoverageComment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Report,

        [Parameter(Mandatory)]
        [ValidatePattern('^[^/]+/[^/]+$')]
        [string] $Repository,

        [Parameter(Mandatory)]
        [ValidateRange(1, [int]::MaxValue)]
        [int] $PullRequestNumber,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Token,

        [ValidateRange(100, 5000)]
        [int] $CommentSafetyCap = 5000,

        [scriptblock] $ApiInvoker = ${function:Invoke-CoverageGitHubApi}
    )

    $owner, $name = $Repository.Split('/', 2)
    $query = @'
query($owner: String!, $name: String!, $number: Int!, $cursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      comments(first: 100, after: $cursor) {
        nodes {
          databaseId
          body
          author { login __typename }
        }
        pageInfo { hasNextPage endCursor }
      }
    }
  }
}
'@

    $env:GH_TOKEN = $Token
    $comments = [Collections.Generic.List[object]]::new()
    $seenCursors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cursor = $null
    $commentsRead = 0
    do {
        $response = & $ApiInvoker ([pscustomobject]@{
            Kind = 'GraphQL'
            Query = $query
            Owner = $owner
            Name = $name
            Number = $PullRequestNumber
            Cursor = $cursor
        })

        $pullRequest = $response.data.repository.pullRequest
        if ($null -eq $pullRequest) {
            throw "Pull request '$Repository#$PullRequestNumber' was not found."
        }

        $nodes = @($pullRequest.comments.nodes)
        if ($nodes.Count -gt 100) {
            throw 'GitHub returned more than 100 comments in one bounded page.'
        }
        $commentsRead += $nodes.Count
        if ($commentsRead -gt $CommentSafetyCap) {
            throw "Coverage comment lookup exceeded the $CommentSafetyCap-comment safety cap."
        }

        $pageComments = @(
            $nodes |
                ForEach-Object {
                    $author = $_.PSObject.Properties['author']
                    [pscustomobject]@{
                        id = $_.databaseId
                        body = $_.body
                        authorLogin = if ($null -eq $author -or $null -eq $author.Value) {
                            $null
                        } else {
                            $author.Value.login
                        }
                        authorType = if ($null -eq $author -or $null -eq $author.Value) {
                            $null
                        } else {
                            $author.Value.__typename
                        }
                    }
                }
        )
        foreach ($comment in @(
                Get-OwnedCoverageComments `
                    -Comments $pageComments `
                    -Marker '<!-- pitcrew-coverage-report -->' `
                    -ExpectedAuthorLogin 'github-actions[bot]'
            )) {
            $comments.Add($comment)
        }

        $hasNextPage = $pullRequest.comments.pageInfo.hasNextPage
        if ($hasNextPage -isnot [bool]) {
            throw 'GitHub returned malformed coverage comment pagination data.'
        }
        if ($hasNextPage) {
            $endCursor = $pullRequest.comments.pageInfo.endCursor
            if (
                $endCursor -isnot [string] -or
                [string]::IsNullOrWhiteSpace($endCursor) -or
                $endCursor -ceq $cursor -or
                -not $seenCursors.Add($endCursor)
            ) {
                throw 'Coverage comment pagination cursor did not progress.'
            }
            $cursor = $endCursor
        }
    } while ($hasNextPage)

    $plan = Get-CoverageCommentPlan `
        -Comments $comments `
        -Report $Report `
        -Marker '<!-- pitcrew-coverage-report -->' `
        -ExpectedAuthorLogin 'github-actions[bot]'

    foreach ($commentId in $plan.DeleteCommentIds) {
        [void](& $ApiInvoker ([pscustomobject]@{
            Kind = 'REST'
            Endpoint = "repos/$Repository/issues/comments/$commentId"
            Method = 'DELETE'
            Body = $null
        }))
    }

    $endpoint = if ($plan.Method -ceq 'POST') {
        "repos/$Repository/issues/$PullRequestNumber/comments"
    } else {
        "repos/$Repository/issues/comments/$($plan.CommentId)"
    }
    [void](& $ApiInvoker ([pscustomobject]@{
        Kind = 'REST'
        Endpoint = $endpoint
        Method = $plan.Method
        Body = $plan.Body
    }))

    return $plan
}

Export-ModuleMember -Function `
    Get-CoverageCommentPlan, `
    Publish-CoverageComment
