#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptPath = Join-Path (
    Split-Path $PSScriptRoot -Parent
) 'scripts' 'Initialize-PitCrewHostedSupportPlane.ps1'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "pitcrew-hosted-support-$([Guid]::NewGuid().ToString('N'))"

function Invoke-Initializer {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [string]$Version = '0.12.2',

        [string]$Domain = 'relay.example.com'
    )

    return & $scriptPath `
        -Version $Version `
        -RelayDomain $Domain `
        -EnvFile $Path
}

function Get-EnvironmentValues {
    param([Parameter(Mandatory)][string]$Path)

    $result = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            continue
        }
        $result[$line.Substring(0, $separator)] =
            $line.Substring($separator + 1)
    }
    return $result
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [Parameter(Mandatory)]
        [string]$ExpectedMessage
    )

    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notmatch $ExpectedMessage) {
            throw "Expected '$ExpectedMessage', received '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected failure matching '$ExpectedMessage'."
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

    $freshPath = Join-Path $temporaryRoot '.env.hosted'
    [IO.File]::WriteAllText(
        $freshPath,
        "PITCREW_DASHBOARD_VERSION=0.12.1`nPITCREW_GITHUB_CLIENT_SECRET=preserve-me`n",
        [Text.UTF8Encoding]::new($false))
    $created = Invoke-Initializer -Path $freshPath
    if (-not $created.Updated -or
        $created.ExistingConfigurationValidated -or
        $created.SecretValuesEmitted) {
        throw 'Fresh initialization returned an invalid result contract.'
    }
    $createdValues = Get-EnvironmentValues -Path $freshPath
    if ($createdValues.PITCREW_GITHUB_CLIENT_SECRET -cne 'preserve-me' -or
        $createdValues.PITCREW_SUPPORT_RELAY_VERSION -cne '0.12.2' -or
        $createdValues.PITCREW_SUPPORT_RELAY_DOMAIN -cne 'relay.example.com') {
        throw 'Fresh initialization did not preserve unrelated values or set public support values.'
    }
    foreach ($key in @(
        'PITCREW_SUPPORT_RELAY_INTERNAL_BEARER_SECRET'
        'PITCREW_SUPPORT_AUTHORIZATION_SIGNING_PRIVATE_KEY_PKCS8'
        'PITCREW_SUPPORT_RESULT_DECRYPTION_PRIVATE_KEY_PKCS8'
    )) {
        if ([string]::IsNullOrWhiteSpace([string]$createdValues[$key])) {
            throw "Fresh initialization did not populate '$key'."
        }
    }

    $beforeIdempotent = [IO.File]::ReadAllText($freshPath)
    $validated = Invoke-Initializer -Path $freshPath
    $afterIdempotent = [IO.File]::ReadAllText($freshPath)
    if ($validated.Updated -or
        -not $validated.ExistingConfigurationValidated -or
        $beforeIdempotent -cne $afterIdempotent) {
        throw 'Existing valid configuration was not handled idempotently.'
    }

    $partialPath = Join-Path $temporaryRoot 'partial.env.hosted'
    [IO.File]::WriteAllText(
        $partialPath,
        "PITCREW_SUPPORT_RELAY_INTERNAL_BEARER_SECRET=partial-secret-value`n",
        [Text.UTF8Encoding]::new($false))
    $partialBefore = [IO.File]::ReadAllText($partialPath)
    Assert-Throws `
        -Action { Invoke-Initializer -Path $partialPath | Out-Null } `
        -ExpectedMessage 'configuration is partial'
    if ([IO.File]::ReadAllText($partialPath) -cne $partialBefore) {
        throw 'Partial configuration changed after rejection.'
    }

    $duplicatePath = Join-Path $temporaryRoot 'duplicate.env.hosted'
    [IO.File]::WriteAllText(
        $duplicatePath,
        "PITCREW_SUPPORT_RELAY_DOMAIN=one.example.com`nPITCREW_SUPPORT_RELAY_DOMAIN=two.example.com`n",
        [Text.UTF8Encoding]::new($false))
    Assert-Throws `
        -Action { Invoke-Initializer -Path $duplicatePath | Out-Null } `
        -ExpectedMessage 'duplicate'

    Assert-Throws `
        -Action {
            Invoke-Initializer `
                -Path $partialPath `
                -Domain 'https://relay.example.com' |
                Out-Null
        } `
        -ExpectedMessage 'RelayDomain'

    Write-Host 'Hosted support-plane configuration tests passed.'
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
