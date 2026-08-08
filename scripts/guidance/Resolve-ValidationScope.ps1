#Requires -Version 7.0
<#
.SYNOPSIS
    Resolve Dashboard CI scope for one workflow event.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('pull_request', 'push', 'workflow_dispatch')]
    [string]$EventName,

    [string]$RequestedScope = '',
    [bool]$IsDraft = $false,

    [ValidateSet('full', 'subset', 'ready-only')]
    [string]$DraftMode = 'subset',

    [string[]]$ChangedFiles = @(),
    [switch]$Conservative
)

$ErrorActionPreference = 'Stop'

if ($EventName -eq 'workflow_dispatch') {
    $scope = if ($RequestedScope) { $RequestedScope } else { 'full' }
    if ($scope -notin @('full', 'subset')) {
        Write-Error "Unsupported requested validation scope '$scope'."
    }
    return $scope
}

if ($EventName -eq 'push') {
    return 'full'
}

if ($IsDraft) {
    return $DraftMode
}

return 'full'
