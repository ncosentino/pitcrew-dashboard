#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "pitcrew-coverlet-merge-$([guid]::NewGuid())"

function Invoke-CoverageRun {
    param(
        [Parameter(Mandatory)]
        [string] $Project,

        [Parameter(Mandatory)]
        [string] $ResultsDirectory,

        [Parameter(Mandatory)]
        [string] $FilePrefix
    )

    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
    dotnet test `
        --project $Project `
        --configuration Release `
        --results-directory $ResultsDirectory `
        -- `
        --coverlet `
        --coverlet-output-format cobertura `
        --coverlet-output-format opencover `
        --coverlet-file-prefix $FilePrefix `
        --coverlet-include '[PitCrew.CoverageMergeFixture]*' `
        --coverlet-exclude '[*.Tests]*' `
        --coverlet-exclude-by-attribute GeneratedCodeAttribute `
        --coverlet-exclude-by-attribute CompilerGeneratedAttribute `
        --coverlet-exclude-by-file '**/*.g.cs' `
        --coverlet-exclude-by-file '**/*.generated.cs' `
        --coverlet-exclude-by-file '**/obj/**' `
        --coverlet-exclude-assemblies-without-sources MissingAll |
        Out-Host
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $reports = @(
        Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -File -Filter '*.xml'
    )
    $coberturaReports = @(
        $reports |
            Where-Object {
                $_.Name -match "^$([regex]::Escape($FilePrefix))\.coverage\.cobertura\.\d{15}\.xml$"
            }
    )
    $openCoverReports = @(
        $reports |
            Where-Object {
                $_.Name -match "^$([regex]::Escape($FilePrefix))\.coverage\.opencover\.\d{15}\.xml$"
            }
    )
    if ($coberturaReports.Count -ne 1 -or $openCoverReports.Count -ne 1) {
        throw (
            'Expected one uniquely prefixed Cobertura and OpenCover report, found ' +
            "$($coberturaReports.Count) and $($openCoverReports.Count)."
        )
    }

    return [pscustomobject]@{
        Cobertura = $coberturaReports[0].FullName
        OpenCover = $openCoverReports[0].FullName
    }
}

function Get-BranchTotals {
    param(
        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    [xml]$document = Get-Content -LiteralPath $ReportPath -Raw -Encoding utf8
    $covered = 0
    $total = 0
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($class in @($document.SelectNodes('//class'))) {
        $filename = $class.GetAttribute('filename').Replace('\', '/')
        if (-not $filename.EndsWith('/BranchChoice.cs', [StringComparison]::Ordinal)) {
            continue
        }
        foreach ($line in @($class.SelectNodes('./lines/line'))) {
            if (-not $line.GetAttribute('branch').Equals(
                    'true',
                    [StringComparison]::OrdinalIgnoreCase
                )) {
                continue
            }
            $key = "$filename`:$($line.GetAttribute('number'))"
            if (-not $seen.Add($key)) {
                continue
            }
            $match = [regex]::Match(
                $line.GetAttribute('condition-coverage'),
                '^\d+(?:\.\d+)?%\s+\((?<covered>\d+)\/(?<total>\d+)\)$'
            )
            if (-not $match.Success) {
                throw "Report '$ReportPath' contains malformed condition coverage."
            }
            $covered += [int]$match.Groups['covered'].Value
            $total += [int]$match.Groups['total'].Value
        }
    }
    if ($total -eq 0) {
        throw "Report '$ReportPath' contains no fixture branch conditions."
    }

    return [pscustomobject]@{ Covered = $covered; Total = $total }
}

try {
    $targetRoot = Join-Path $fixtureRoot 'src/PitCrew.CoverageMergeFixture'
    $leftRoot = Join-Path $fixtureRoot 'tests/PitCrew.CoverageMergeFixture.Left.Tests'
    $rightRoot = Join-Path $fixtureRoot 'tests/PitCrew.CoverageMergeFixture.Right.Tests'
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $leftRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $rightRoot -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $targetRoot 'PitCrew.CoverageMergeFixture.csproj') -Encoding utf8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath (Join-Path $targetRoot 'BranchChoice.cs') -Encoding utf8 -Value @'
namespace PitCrew.CoverageMergeFixture;

public static class BranchChoice
{
    public static string Choose(bool chooseLeft)
    {
        if (chooseLeft)
        {
            return "left";
        }

        return "right";
    }
}
'@

    $testProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.MTP" Version="10.0.1" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.6.0" />
    <PackageReference Include="TUnit" Version="1.53.0" />
    <ProjectReference Include="..\..\src\PitCrew.CoverageMergeFixture\PitCrew.CoverageMergeFixture.csproj" />
  </ItemGroup>
</Project>
'@
    Set-Content `
        -LiteralPath (Join-Path $leftRoot 'PitCrew.CoverageMergeFixture.Left.Tests.csproj') `
        -Encoding utf8 `
        -Value $testProject
    Set-Content `
        -LiteralPath (Join-Path $rightRoot 'PitCrew.CoverageMergeFixture.Right.Tests.csproj') `
        -Encoding utf8 `
        -Value $testProject
    Set-Content -LiteralPath (Join-Path $leftRoot 'BranchChoiceTests.cs') -Encoding utf8 -Value @'
using PitCrew.CoverageMergeFixture;
using TUnit.Assertions;
using TUnit.Core;

public sealed class BranchChoiceTests
{
    [Test]
    public async Task CoversLeftBranch()
    {
        await Assert.That(BranchChoice.Choose(true)).IsEqualTo("left");
    }
}
'@
    Set-Content -LiteralPath (Join-Path $rightRoot 'BranchChoiceTests.cs') -Encoding utf8 -Value @'
using PitCrew.CoverageMergeFixture;
using TUnit.Assertions;
using TUnit.Core;

public sealed class BranchChoiceTests
{
    [Test]
    public async Task CoversRightBranch()
    {
        await Assert.That(BranchChoice.Choose(false)).IsEqualTo("right");
    }
}
'@

    Push-Location $repositoryRoot
    try {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }

        $leftRun = Invoke-CoverageRun `
            -Project (Join-Path $leftRoot 'PitCrew.CoverageMergeFixture.Left.Tests.csproj') `
            -ResultsDirectory (Join-Path $fixtureRoot 'results/left') `
            -FilePrefix 'left'
        $rightRun = Invoke-CoverageRun `
            -Project (Join-Path $rightRoot 'PitCrew.CoverageMergeFixture.Right.Tests.csproj') `
            -ResultsDirectory (Join-Path $fixtureRoot 'results/right') `
            -FilePrefix 'right'

        $mergedDirectory = Join-Path $fixtureRoot 'results/merged'
        New-Item -ItemType Directory -Path $mergedDirectory | Out-Null
        dotnet tool run reportgenerator -- `
            "-reports:$($leftRun.OpenCover);$($rightRun.OpenCover)" `
            "-targetdir:$mergedDirectory" `
            '-reporttypes:Cobertura' `
            '-verbosity:Off'
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    } finally {
        Pop-Location
    }

    $mergedReport = Join-Path $mergedDirectory 'Cobertura.xml'
    if (-not (Test-Path -LiteralPath $mergedReport -PathType Leaf)) {
        throw "Merged Cobertura report '$mergedReport' was not created."
    }

    $leftBranches = Get-BranchTotals -ReportPath $leftRun.Cobertura
    $rightBranches = Get-BranchTotals -ReportPath $rightRun.Cobertura
    $mergedBranches = Get-BranchTotals -ReportPath $mergedReport
    if (
        $leftBranches.Covered -ge $leftBranches.Total -or
        $mergedBranches.Covered -ne $mergedBranches.Total -or
        $mergedBranches.Total -ne $leftBranches.Total
    ) {
        throw (
            "ReportGenerator did not union complementary branches: " +
            "left=$($leftBranches.Covered)/$($leftBranches.Total), " +
            "right=$($rightBranches.Covered)/$($rightBranches.Total), " +
            "merged=$($mergedBranches.Covered)/$($mergedBranches.Total)."
        )
    }

    $frontendSummary = Join-Path $fixtureRoot 'coverage-summary.json'
    @{
        total = @{
            lines = @{ total = 1; covered = 1 }
            statements = @{ total = 1; covered = 1 }
            functions = @{ total = 1; covered = 1 }
            branches = @{ total = 1; covered = 1 }
        }
        'C:/repo/src/PitCrew.Dashboard.WebApi/ClientApp/src/App.tsx' = @{
            lines = @{ total = 1; covered = 1 }
            statements = @{ total = 1; covered = 1 }
            functions = @{ total = 1; covered = 1 }
            branches = @{ total = 1; covered = 1 }
        }
    } | ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $frontendSummary -Encoding utf8
    $changedFiles = Join-Path $fixtureRoot 'changed-files.json'
    @('src/PitCrew.CoverageMergeFixture/BranchChoice.cs') | ConvertTo-Json |
        Set-Content -LiteralPath $changedFiles -Encoding utf8

    Import-Module (Join-Path $repositoryRoot 'scripts/coverage/CoverageReport.psm1') -Force
    $report = New-CoverageReport `
        -CoberturaPath $mergedReport `
        -FrontendSummaryPath $frontendSummary `
        -ChangedFilesPath $changedFiles `
        -RepositoryRoot $fixtureRoot `
        -RunUrl 'https://github.com/ncosentino/pitcrew-dashboard/actions/runs/123'
    if ($report -notmatch '\| C# \| 100\.00% \| 100\.00% \| 100\.00% \|') {
        throw 'The production reporter did not read the merged Coverlet condition totals.'
    }

    Write-Host (
        "Coverlet MTP merge: two reports combined complementary branches from " +
        "$($leftBranches.Covered)/$($leftBranches.Total) to " +
        "$($mergedBranches.Covered)/$($mergedBranches.Total); reporter emitted 100.00%.")
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
