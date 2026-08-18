#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$RelayDomain,

    [string]$EnvFile = '.env.hosted'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-Base64Url {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).
        TrimEnd('=').
        Replace('+', '-').
        Replace('/', '_')
}

function ConvertFrom-Base64Url {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '^[A-Za-z0-9_-]+$') {
        throw 'Support key material is not valid base64url.'
    }
    $padded = $Value.Replace('-', '+').Replace('_', '/')
    $padding = (4 - ($padded.Length % 4)) % 4
    return ,([Convert]::FromBase64String($padded + ('=' * $padding)))
}

function Test-SupportKeys {
    param(
        [Parameter(Mandatory)]
        [string]$SigningKey,

        [Parameter(Mandatory)]
        [string]$DecryptionKey
    )

    $signingBytes = ConvertFrom-Base64Url -Value $SigningKey
    $decryptionBytes = ConvertFrom-Base64Url -Value $DecryptionKey
    try {
        $bytesRead = 0
        $ecdsa = [Security.Cryptography.ECDsa]::Create()
        try {
            $ecdsa.ImportPkcs8PrivateKey($signingBytes, [ref]$bytesRead)
            $signingParameters = $ecdsa.ExportParameters($false)
            if ($bytesRead -ne $signingBytes.Length -or
                $ecdsa.KeySize -ne 256 -or
                $signingParameters.Curve.Oid.Value -cne
                    '1.2.840.10045.3.1.7') {
                throw 'The support authorization key is not an ECDSA P-256 PKCS#8 key.'
            }
        } finally {
            $ecdsa.Dispose()
        }

        $bytesRead = 0
        $rsa = [Security.Cryptography.RSA]::Create()
        try {
            $rsa.ImportPkcs8PrivateKey($decryptionBytes, [ref]$bytesRead)
            if ($bytesRead -ne $decryptionBytes.Length -or
                $rsa.KeySize -ne 3072) {
                throw 'The support result key is not an RSA-3072 PKCS#8 key.'
            }
        } finally {
            $rsa.Dispose()
        }
    } finally {
        [Array]::Clear($signingBytes, 0, $signingBytes.Length)
        [Array]::Clear($decryptionBytes, 0, $decryptionBytes.Length)
    }
}

$RelayDomain = $RelayDomain.ToLowerInvariant()
if ([Uri]::CheckHostName($RelayDomain) -ne [UriHostNameType]::Dns -or
    -not $RelayDomain.Contains('.', [StringComparison]::Ordinal) -or
    $RelayDomain.Length -gt 253 -or
    $RelayDomain.EndsWith('.')) {
    throw 'RelayDomain must be one fully qualified DNS hostname without a trailing dot.'
}

$resolvedEnvFile = (Resolve-Path -LiteralPath $EnvFile).Path
$envItem = Get-Item -LiteralPath $resolvedEnvFile
if (($envItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'The hosted environment file cannot be a symbolic link or reparse point.'
}

$content = [IO.File]::ReadAllText(
    $resolvedEnvFile,
    [Text.UTF8Encoding]::new($false, $true))
$newline = if ($content.Contains("`r`n", [StringComparison]::Ordinal)) {
    "`r`n"
} else {
    "`n"
}
$hadTrailingNewline = $content.EndsWith("`n", [StringComparison]::Ordinal) -or
    $content.EndsWith("`r", [StringComparison]::Ordinal)
$lines = [Collections.Generic.List[string]]::new()
foreach ($line in [Regex]::Split($content, '\r\n|\n|\r')) {
    $lines.Add($line)
}
if ($hadTrailingNewline -and
    $lines.Count -gt 0 -and
    $lines[$lines.Count - 1] -eq '') {
    $lines.RemoveAt($lines.Count - 1)
}

$keys = @(
    'PITCREW_SUPPORT_RELAY_VERSION'
    'PITCREW_SUPPORT_RELAY_DOMAIN'
    'PITCREW_SUPPORT_RELAY_INTERNAL_BEARER_SECRET'
    'PITCREW_SUPPORT_AUTHORIZATION_SIGNING_PRIVATE_KEY_PKCS8'
    'PITCREW_SUPPORT_RESULT_DECRYPTION_PRIVATE_KEY_PKCS8'
)
$indexes = @{}
$values = @{}
for ($index = 0; $index -lt $lines.Count; $index++) {
    $match = [Regex]::Match(
        $lines[$index],
        '^(?<key>PITCREW_SUPPORT_[A-Z0-9_]+)=(?<value>.*)$')
    if (-not $match.Success -or $match.Groups['key'].Value -notin $keys) {
        continue
    }
    $key = $match.Groups['key'].Value
    if ($indexes.ContainsKey($key)) {
        throw "Hosted environment contains duplicate '$key' entries."
    }
    $indexes[$key] = $index
    $values[$key] = $match.Groups['value'].Value
}

$secretKeys = @(
    'PITCREW_SUPPORT_RELAY_INTERNAL_BEARER_SECRET'
    'PITCREW_SUPPORT_AUTHORIZATION_SIGNING_PRIVATE_KEY_PKCS8'
    'PITCREW_SUPPORT_RESULT_DECRYPTION_PRIVATE_KEY_PKCS8'
)
$configuredSecretCount = @(
    $secretKeys |
        Where-Object {
            $values.ContainsKey($_) -and
            -not [string]::IsNullOrWhiteSpace([string]$values[$_])
        }
).Count

if ($configuredSecretCount -notin @(0, $secretKeys.Count)) {
    throw 'Hosted support-plane configuration is partial; preserve it for operator review.'
}

$updated = $false
if ($configuredSecretCount -eq $secretKeys.Count) {
    if ([string]$values['PITCREW_SUPPORT_RELAY_VERSION'] -cne $Version -or
        [string]$values['PITCREW_SUPPORT_RELAY_DOMAIN'] -cne $RelayDomain) {
        throw 'Existing hosted support-plane configuration targets a different version or relay domain.'
    }
    $bearer = [string]$values['PITCREW_SUPPORT_RELAY_INTERNAL_BEARER_SECRET']
    if ($bearer.Length -lt 16 -or
        $bearer.Length -gt 4096 -or
        $bearer -match '[\r\n]') {
        throw 'The existing support relay bearer does not satisfy the bounded secret contract.'
    }
    Test-SupportKeys `
        -SigningKey ([string]$values['PITCREW_SUPPORT_AUTHORIZATION_SIGNING_PRIVATE_KEY_PKCS8']) `
        -DecryptionKey ([string]$values['PITCREW_SUPPORT_RESULT_DECRYPTION_PRIVATE_KEY_PKCS8'])
} else {
    $bearerBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $signingBytes = $null
    $decryptionBytes = $null
    $ecdsa = [Security.Cryptography.ECDsa]::Create(
        [Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try {
        $signingBytes = $ecdsa.ExportPkcs8PrivateKey()
        $decryptionBytes = $rsa.ExportPkcs8PrivateKey()
        $generated = [ordered]@{
            PITCREW_SUPPORT_RELAY_VERSION = $Version
            PITCREW_SUPPORT_RELAY_DOMAIN = $RelayDomain
            PITCREW_SUPPORT_RELAY_INTERNAL_BEARER_SECRET =
                ConvertTo-Base64Url -Bytes $bearerBytes
            PITCREW_SUPPORT_AUTHORIZATION_SIGNING_PRIVATE_KEY_PKCS8 =
                ConvertTo-Base64Url -Bytes $signingBytes
            PITCREW_SUPPORT_RESULT_DECRYPTION_PRIVATE_KEY_PKCS8 =
                ConvertTo-Base64Url -Bytes $decryptionBytes
        }

        foreach ($key in $keys) {
            $line = "$key=$($generated[$key])"
            if ($indexes.ContainsKey($key)) {
                $lines[$indexes[$key]] = $line
            } else {
                $indexes[$key] = $lines.Count
                $lines.Add($line)
            }
        }

        $rendered = [string]::Join($newline, $lines)
        if ($hadTrailingNewline) {
            $rendered += $newline
        }
        $backupPath = "$resolvedEnvFile.support-plane-backup"
        if (Test-Path -LiteralPath $backupPath) {
            throw 'A hosted support-plane initialization backup already exists.'
        }
        [IO.File]::Copy($resolvedEnvFile, $backupPath, $false)
        $removeBackup = $false
        try {
            [IO.File]::WriteAllText(
                $resolvedEnvFile,
                $rendered,
                [Text.UTF8Encoding]::new($false))
            Test-SupportKeys `
                -SigningKey ([string]$generated.PITCREW_SUPPORT_AUTHORIZATION_SIGNING_PRIVATE_KEY_PKCS8) `
                -DecryptionKey ([string]$generated.PITCREW_SUPPORT_RESULT_DECRYPTION_PRIVATE_KEY_PKCS8)
            $updated = $true
            $removeBackup = $true
        } catch {
            try {
                [IO.File]::Copy($backupPath, $resolvedEnvFile, $true)
                $removeBackup = $true
            } catch {
                throw 'Hosted support-plane initialization and environment restoration both failed; the backup was retained.'
            }
            throw
        } finally {
            if ($removeBackup -and (Test-Path -LiteralPath $backupPath)) {
                Remove-Item -LiteralPath $backupPath -Force
            }
        }
    } finally {
        $ecdsa.Dispose()
        $rsa.Dispose()
        [Array]::Clear($bearerBytes, 0, $bearerBytes.Length)
        if ($null -ne $signingBytes) {
            [Array]::Clear($signingBytes, 0, $signingBytes.Length)
        }
        if ($null -ne $decryptionBytes) {
            [Array]::Clear($decryptionBytes, 0, $decryptionBytes.Length)
        }
    }
}

[PSCustomObject]@{
    Updated = $updated
    ExistingConfigurationValidated = -not $updated
    RelayVersion = $Version
    RelayDomainConfigured = $true
    SecretValuesEmitted = $false
}
