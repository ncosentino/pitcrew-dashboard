#Requires -Version 7.0
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
    [string] $Token
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'CoverageComment.psm1') -Force

$plan = Publish-CoverageComment `
    -Report $Report `
    -Repository $Repository `
    -PullRequestNumber $PullRequestNumber `
    -Token $Token

Write-Host "Coverage comment $($plan.Method.ToLowerInvariant()) completed."
