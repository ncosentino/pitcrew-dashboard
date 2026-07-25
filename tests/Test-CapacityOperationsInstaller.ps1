#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ConnectorPublishRoot = '',

    [switch]$AllowMachineChanges
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $AllowMachineChanges) {
    throw 'Pass -AllowMachineChanges to run the host-service installation tests.'
}
if (-not ($IsWindows -or $IsLinux)) {
    throw 'Capacity-operations installer tests support Windows and Linux only.'
}
if ($IsWindows) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole(
                [Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Windows installer tests require an elevated PowerShell session.'
        }
    } finally {
        $identity.Dispose()
    }
} elseif ([int](& id -u) -ne 0) {
    throw 'Linux installer tests require root.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installerPath = Join-Path $repositoryRoot 'scripts' 'Enable-PitCrewCapacityOperations.ps1'
$errors = [System.Collections.Generic.List[string]]::new()
$checks = 0
$version = '9.9.9'
$serviceName = 'PitCrewConnector'

function Add-Check {
    param(
        [object]$Condition,
        [string]$Failure
    )

    $script:checks++
    if (-not $Condition) {
        $script:errors.Add($Failure)
    }
}

function Get-HostInstallationPaths {
    if ($IsWindows) {
        return @{
            InstallRoot = Join-Path (
                [Environment]::GetFolderPath(
                    [Environment+SpecialFolder]::ProgramFiles)
            ) 'PitCrew\Connector'
            DataRoot = Join-Path (
                [Environment]::GetFolderPath(
                    [Environment+SpecialFolder]::CommonApplicationData)
            ) 'PitCrew\Connector'
            EnvironmentPath = $null
            ServicePath = $null
        }
    }
    return @{
        InstallRoot = '/opt/pitcrew-connector'
        DataRoot = '/var/lib/pitcrew-connector'
        EnvironmentPath = '/etc/pitcrew-connector.env'
        ServicePath = '/etc/systemd/system/pitcrew-connector.service'
    }
}

function Remove-TestHostInstallation {
    $paths = Get-HostInstallationPaths
    if ($IsWindows) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -ne $service) {
            try {
                if ($service.Status -ne
                    [ServiceProcess.ServiceControllerStatus]::Stopped) {
                    Stop-Service -Name $serviceName -Force -ErrorAction Stop
                    $service.WaitForStatus(
                        [ServiceProcess.ServiceControllerStatus]::Stopped,
                        [TimeSpan]::FromSeconds(30))
                }
            } finally {
                $service.Dispose()
            }
            & sc.exe delete $serviceName | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Could not delete test service '$serviceName'."
            }
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
            while ($true) {
                $remainingService = Get-Service `
                    -Name $serviceName `
                    -ErrorAction SilentlyContinue
                if ($null -eq $remainingService) {
                    break
                }
                $remainingService.Dispose()
                if ([DateTimeOffset]::UtcNow -ge $deadline) {
                    throw "Test service '$serviceName' remained registered."
                }
                Start-Sleep -Milliseconds 250
            }
        }
    } elseif (Test-Path -LiteralPath $paths.ServicePath -PathType Leaf) {
        & systemctl disable --now pitcrew-connector.service 2>$null | Out-Null
    }
    foreach ($path in @(
        $paths.InstallRoot,
        $paths.DataRoot,
        $paths.EnvironmentPath,
        $paths.ServicePath
    )) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and
            (Test-Path -LiteralPath $path)) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    if ($IsLinux) {
        & systemctl daemon-reload 2>$null | Out-Null
    }
}

function New-TestReleaseAsset {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $architecture = [Runtime.InteropServices.RuntimeInformation]::
        OSArchitecture.ToString().ToLowerInvariant()
    $rid = if ($IsWindows) {
        "win-$architecture"
    } else {
        "linux-$architecture"
    }
    $assetName = "pitcrew-connector-$version-$rid.tar.gz"
    $payloadRoot = Join-Path $Root 'payload'
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
    if ($IsWindows) {
        if ([string]::IsNullOrWhiteSpace($ConnectorPublishRoot)) {
            throw 'ConnectorPublishRoot is required for Windows installer tests.'
        }
        Copy-Item `
            -Path (Join-Path (Resolve-Path $ConnectorPublishRoot).Path '*') `
            -Destination $payloadRoot `
            -Recurse `
            -Force
    } else {
        $executable = Join-Path $payloadRoot 'PitCrew.Connector.App'
        [IO.File]::WriteAllText(
            $executable,
            "#!/bin/sh`nwhile true; do sleep 60; done`n",
            [Text.UTF8Encoding]::new($false))
    }

    $assetPath = Join-Path $Root $assetName
    & tar -C $payloadRoot -czf $assetPath .
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the connector test archive.'
    }
    $checksumPath = "$assetPath.sha256"
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $checksumPath,
        "$hash  $assetName",
        [Text.UTF8Encoding]::new($false))
    return @{
        AssetPath = $assetPath
        ChecksumPath = $checksumPath
    }
}

function Invoke-InstallerScenario {
    param(
        [Parameter(Mandatory)]
        [string]$PitCrewRoot
    )

    & $installerPath `
        -Version $version `
        -PitCrewRoot $PitCrewRoot `
        -DashboardUrl 'https://127.0.0.1:9' `
        -Profiles 'copilot-cli' `
        -CapacityMaximumCeiling 30
}

$testRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "pitcrew-installer-test-$([Guid]::NewGuid().ToString('N'))"
$global:PitCrewInstallerDockerStops = 0
$global:PitCrewInstallerDockerCopies = 0
$global:PitCrewInstallerDockerStarts = 0
$global:PitCrewInstallerFailServiceStart = $false

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    Remove-TestHostInstallation
    $release = New-TestReleaseAsset -Root $testRoot
    $pitCrewRoot = Join-Path $testRoot 'pitcrew'
    $profileRoot = Join-Path $pitCrewRoot '.pitcrew-state' 'copilot-cli'
    New-Item -ItemType Directory -Path $profileRoot -Force | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $pitCrewRoot 'Setup-Runner.ps1'),
        '',
        [Text.UTF8Encoding]::new($false))
    $identityDocument = @{
        connectorInstanceId = 'installer-test'
        nodeId = '11111111-1111-1111-1111-111111111111'
        credential = 'test-credential'
    } | ConvertTo-Json -Compress

    $global:PitCrewInstallerAssetPath = $release.AssetPath
    $global:PitCrewInstallerChecksumPath = $release.ChecksumPath
    $global:PitCrewInstallerIdentityDocument = $identityDocument

    function global:Invoke-WebRequest {
        param(
            [Parameter(Mandatory)]
            [string]$Uri,

            [Parameter(Mandatory)]
            [string]$OutFile
        )

        $source = if ($Uri.EndsWith(
                '.sha256',
                [StringComparison]::OrdinalIgnoreCase)) {
            $global:PitCrewInstallerChecksumPath
        } else {
            $global:PitCrewInstallerAssetPath
        }
        Copy-Item -LiteralPath $source -Destination $OutFile -Force
    }

    function global:docker {
        $command = [string]$args[0]
        $global:LASTEXITCODE = 0
        switch ($command) {
            'ps' {
                'installer-test-container'
            }
            'stop' {
                $global:PitCrewInstallerDockerStops++
            }
            'cp' {
                $destination = [string]$args[-1]
                [IO.File]::WriteAllText(
                    $destination,
                    $global:PitCrewInstallerIdentityDocument,
                    [Text.UTF8Encoding]::new($false))
                $global:PitCrewInstallerDockerCopies++
            }
            'start' {
                $global:PitCrewInstallerDockerStarts++
            }
            default {
                throw "Unexpected docker command '$command'."
            }
        }
    }

    Invoke-InstallerScenario -PitCrewRoot $pitCrewRoot
    $paths = Get-HostInstallationPaths
    Add-Check (
        $global:PitCrewInstallerDockerStops -eq 1
    ) 'The installer did not stop exactly one connector container.'
    Add-Check (
        $global:PitCrewInstallerDockerCopies -eq 1
    ) 'The installer did not migrate the connector identity.'
    Add-Check (
        $global:PitCrewInstallerDockerStarts -eq 0
    ) 'The installer restarted the connector container after successful service startup.'
    Add-Check (
        Test-Path -LiteralPath (Join-Path $paths.DataRoot 'identity.json') -PathType Leaf
    ) 'The migrated connector identity is missing.'

    if ($IsWindows) {
        $service = Get-Service -Name $serviceName
        try {
            Add-Check (
                $service.Status -eq
                [ServiceProcess.ServiceControllerStatus]::Running
            ) 'The Windows connector service is not running.'
        } finally {
            $service.Dispose()
        }
        $serviceConfiguration = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='$serviceName'"
        Add-Check (
            $serviceConfiguration.StartName -eq 'LocalSystem'
        ) 'The Windows connector service does not use LocalSystem.'
        Add-Check (
            $serviceConfiguration.PathName -match '--contentRoot' -and
            $serviceConfiguration.PathName.Contains(
                $paths.InstallRoot,
                [StringComparison]::OrdinalIgnoreCase)
        ) 'The Windows connector service does not pin the application content root.'
        $settings = Get-Content `
            -LiteralPath (Join-Path $paths.InstallRoot 'appsettings.json') `
            -Raw `
            -Encoding UTF8 |
            ConvertFrom-Json -Depth 20
        Add-Check (
            $settings.PitCrew.Connector.OperatorModeEnabled -eq $true
        ) 'The Windows connector configuration did not enable operator mode.'
        Add-Check (
            (@($settings.PitCrew.Connector.AllowedCapacityProfiles) -join ',') -eq
            'copilot-cli'
        ) 'The Windows connector configuration did not preserve the profile allowlist.'
        Add-Check (
            $settings.PitCrew.Connector.CapacityMaximumCeiling -eq 30
        ) 'The Windows connector configuration did not preserve the capacity ceiling.'
        $fileSink = @($settings.Serilog.WriteTo) |
            Where-Object Name -eq 'File' |
            Select-Object -First 1
        Add-Check (
            $null -ne $fileSink -and
            ([string]$fileSink.Args.path).StartsWith(
                $paths.DataRoot,
                [StringComparison]::OrdinalIgnoreCase)
        ) 'The Windows connector configuration does not retain protected service logs.'
        $identityAcl = Get-Acl -LiteralPath (
            Join-Path $paths.DataRoot 'identity.json')
        $broadReadSids = @(
            'S-1-1-0',
            'S-1-5-11',
            'S-1-5-32-545'
        )
        $broadReadRules = @(
            $identityAcl.GetAccessRules(
                $true,
                $true,
                [Security.Principal.SecurityIdentifier]) |
                Where-Object {
                    $_.AccessControlType -eq
                        [Security.AccessControl.AccessControlType]::Allow -and
                    $_.IdentityReference.Value -in $broadReadSids
                }
        )
        Add-Check (
            $broadReadRules.Count -eq 0
        ) 'The migrated connector identity remains readable by a broad local principal.'
    } else {
        Add-Check (
            Test-Path -LiteralPath $paths.ServicePath -PathType Leaf
        ) 'The systemd service definition is missing.'
        $environment = Get-Content `
            -LiteralPath $paths.EnvironmentPath `
            -Raw `
            -Encoding UTF8
        Add-Check (
            $environment -match
            'PitCrew__Connector__AllowedCapacityProfiles__0="copilot-cli"'
        ) 'The systemd environment did not preserve the profile allowlist.'
    }

    Remove-TestHostInstallation
    $global:PitCrewInstallerDockerStops = 0
    $global:PitCrewInstallerDockerCopies = 0
    $global:PitCrewInstallerDockerStarts = 0
    $global:PitCrewInstallerFailServiceStart = $true

    if ($IsWindows) {
        function global:Start-Service {
            throw 'Simulated Windows service startup failure.'
        }
    } else {
        function global:systemctl {
            $global:LASTEXITCODE = 0
            if ($global:PitCrewInstallerFailServiceStart -and
                $args[0] -eq 'enable') {
                $global:LASTEXITCODE = 1
            }
        }
    }

    $rollbackFailedAsExpected = $false
    $rollbackFailureMessage = ''
    try {
        Invoke-InstallerScenario -PitCrewRoot $pitCrewRoot
    } catch {
        $rollbackFailedAsExpected = $true
        $rollbackFailureMessage = $_.Exception.Message
    } finally {
        if ($IsWindows) {
            Remove-Item Function:\Start-Service -ErrorAction SilentlyContinue
        }
    }
    Add-Check $rollbackFailedAsExpected 'The installer did not report the simulated service startup failure.'
    $expectedRollbackFailure = if ($IsWindows) {
        'Simulated Windows service startup failure'
    } else {
        "'systemctl' exited with code 1"
    }
    Add-Check (
        $rollbackFailureMessage.Contains(
            $expectedRollbackFailure,
            [StringComparison]::Ordinal)
    ) 'The rollback scenario failed before the simulated service startup failure.'
    Add-Check (
        $global:PitCrewInstallerDockerStarts -eq 1
    ) 'The installer did not restore the connector container after startup failure.'
    Add-Check (
        -not (Test-Path -LiteralPath $paths.InstallRoot)
    ) 'The installer left host binaries after rollback.'
    Add-Check (
        -not (Test-Path -LiteralPath $paths.DataRoot)
    ) 'The installer left connector identity data after rollback.'
    if ($IsWindows) {
        Add-Check (
            $null -eq (
                Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            )
        ) 'The installer left the Windows service registered after rollback.'
    }
} finally {
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Item Function:\Invoke-WebRequest -ErrorAction SilentlyContinue
    if ($IsLinux) {
        Remove-Item Function:\systemctl -ErrorAction SilentlyContinue
    }
    Remove-TestHostInstallation
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerAssetPath -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerChecksumPath -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerIdentityDocument -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerDockerStops -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerDockerCopies -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerDockerStarts -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable PitCrewInstallerFailServiceStart -Scope Global -ErrorAction SilentlyContinue
}

if ($errors.Count -gt 0) {
    foreach ($errorMessage in $errors) {
        Write-Host "ERROR: $errorMessage" -ForegroundColor Red
    }
    throw "Capacity-operations installer validation failed with $($errors.Count) error(s)."
}

Write-Host "Capacity-operations installer validation passed: $checks assertions." -ForegroundColor Green
