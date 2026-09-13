Set-StrictMode -Version Latest

function Get-RequiredMetric {
    param(
        [Parameter(Mandatory)]
        [object] $Container,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $property = $Container.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "$Context is missing '$Name'."
    }

    $totalProperty = $property.Value.PSObject.Properties['total']
    $coveredProperty = $property.Value.PSObject.Properties['covered']
    if (
        $null -eq $totalProperty -or
        $null -eq $coveredProperty -or
        $totalProperty.Value -isnot [ValueType] -or
        $coveredProperty.Value -isnot [ValueType]
    ) {
        throw "$Context has malformed '$Name' totals."
    }

    $total = [double]$totalProperty.Value
    $covered = [double]$coveredProperty.Value
    if ($total -lt 0 -or $covered -lt 0 -or $covered -gt $total) {
        throw "$Context has invalid '$Name' totals."
    }

    return [pscustomobject]@{
        Total = $total
        Covered = $covered
        Percent = if ($total -eq 0) { 100.0 } else { 100.0 * $covered / $total }
    }
}

function ConvertTo-RepositoryPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $normalized = $Path.Replace('\', '/')
    $frontendMarker = '/src/PitCrew.Dashboard.WebApi/ClientApp/'
    $markerIndex = $normalized.IndexOf($frontendMarker, [StringComparison]::OrdinalIgnoreCase)
    if ($markerIndex -ge 0) {
        return $normalized.Substring($markerIndex + 1)
    }

    $sourceMarker = '/src/'
    $sourceIndex = $normalized.IndexOf($sourceMarker, [StringComparison]::OrdinalIgnoreCase)
    if ($sourceIndex -ge 0) {
        return $normalized.Substring($sourceIndex + 1)
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $Path).Replace('\', '/')
        if (-not $relative.StartsWith('../', [StringComparison]::Ordinal)) {
            return $relative
        }
    }

    return $normalized.TrimStart('./')
}

function Get-Percentage {
    param(
        [double] $Covered,
        [double] $Total
    )

    if ($Total -eq 0) {
        return 100.0
    }

    return 100.0 * $Covered / $Total
}

function Format-Percentage {
    param([double] $Value)

    return $Value.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + '%'
}

function Escape-MarkdownCell {
    param([AllowEmptyString()][string] $Value)

    return $Value.Replace('|', '\|').Replace("`r", '').Replace("`n", ' ')
}

function Test-CSharpProductionPath {
    param([string] $Path)

    return (
        $Path.StartsWith('src/', [StringComparison]::OrdinalIgnoreCase) -and
        $Path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and
        $Path -notmatch '(?i)(^|/)(?:obj|bin)/' -and
        $Path -notmatch '(?i)(^|/)[^/]*\.Tests?/'
    )
}

function Test-FrontendProductionPath {
    param([string] $Path)

    return (
        $Path.StartsWith(
            'src/PitCrew.Dashboard.WebApi/ClientApp/src/',
            [StringComparison]::OrdinalIgnoreCase
        ) -and
        $Path -match '(?i)\.(?:ts|tsx)$' -and
        $Path -notmatch '(?i)(?:^|/)(?:__tests__|generated)/' -and
        $Path -notmatch '(?i)(?:\.test|\.spec|\.generated|\.d)\.(?:ts|tsx)$' -and
        $Path -notmatch '(?i)/setupTests\.(?:ts|tsx)$'
    )
}

function Get-CSharpCoverage {
    param(
        [Parameter(Mandatory)]
        [string] $CoberturaPath,

        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $lines = @{}
    $branches = @{}
    $functions = @{}
    if (-not (Test-Path -LiteralPath $CoberturaPath -PathType Leaf)) {
        throw "Cobertura report '$CoberturaPath' does not exist."
    }

    try {
        [xml]$document = Get-Content -LiteralPath $CoberturaPath -Raw -Encoding utf8
    } catch {
        throw "Cobertura report '$CoberturaPath' is malformed: $($_.Exception.Message)"
    }

    $classes = @($document.SelectNodes('//class'))
    if ($classes.Count -eq 0) {
        throw "Cobertura report '$CoberturaPath' contains no classes."
    }

    foreach ($class in $classes) {
        $filename = $class.GetAttribute('filename')
        if ([string]::IsNullOrWhiteSpace($filename)) {
            throw "Cobertura report '$CoberturaPath' contains a class without a filename."
        }

        $file = ConvertTo-RepositoryPath -Path $filename -RepositoryRoot $RepositoryRoot
        if (-not (Test-CSharpProductionPath -Path $file)) {
            continue
        }

        $className = $class.GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($className)) {
            throw "Cobertura report '$CoberturaPath' contains a class without a name."
        }

        foreach ($method in @($class.SelectNodes('./methods/method'))) {
            $methodName = $method.GetAttribute('name')
            if ([string]::IsNullOrWhiteSpace($methodName)) {
                throw "Cobertura report '$CoberturaPath' has an unnamed method in '$file'."
            }

            $methodLines = @($method.SelectNodes('./lines/line'))
            if ($methodLines.Count -eq 0) {
                throw (
                    "Cobertura report '$CoberturaPath' has no lines for method " +
                    "'$className.$methodName' in '$file'."
                )
            }

            $methodCovered = $false
            foreach ($methodLine in $methodLines) {
                $methodHits = 0
                if (-not [int]::TryParse($methodLine.GetAttribute('hits'), [ref]$methodHits)) {
                    throw (
                        "Cobertura report '$CoberturaPath' has malformed method coverage " +
                        "for '$className.$methodName' in '$file'."
                    )
                }
                if ($methodHits -gt 0) {
                    $methodCovered = $true
                }
            }

            $functionKey = "$file`:$className`:$methodName`:$($method.GetAttribute('signature'))"
            if ($functions.ContainsKey($functionKey)) {
                $functions[$functionKey] = $functions[$functionKey] -or $methodCovered
            } else {
                $functions[$functionKey] = $methodCovered
            }
        }

        foreach ($line in @($class.SelectNodes('./lines/line'))) {
            $number = 0
            $hits = 0
            if (
                -not [int]::TryParse($line.GetAttribute('number'), [ref]$number) -or
                -not [int]::TryParse($line.GetAttribute('hits'), [ref]$hits)
            ) {
                throw "Cobertura report '$CoberturaPath' has a malformed line in '$file'."
            }

            $lineKey = "$file`:$number"
            if ($lines.ContainsKey($lineKey)) {
                $lines[$lineKey].Covered = $lines[$lineKey].Covered -or ($hits -gt 0)
            } else {
                $lines[$lineKey] = [pscustomobject]@{
                    File = $file
                    Covered = $hits -gt 0
                }
            }

            if ($line.GetAttribute('branch').Equals('true', [StringComparison]::OrdinalIgnoreCase)) {
                $conditionCoverage = $line.GetAttribute('condition-coverage')
                $match = [regex]::Match(
                    $conditionCoverage,
                    '^(?<percent>\d+(?:\.\d+)?)%\s+\((?<covered>\d+)\/(?<total>\d+)\)$',
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant
                )
                if (-not $match.Success) {
                    throw (
                        "Cobertura report '$CoberturaPath' has malformed condition coverage " +
                        "for '$lineKey'."
                    )
                }

                $coveredConditions = 0
                $totalConditions = 0
                $reportedPercent = [decimal]0
                $percentText = $match.Groups['percent'].Value
                $decimalPoint = $percentText.IndexOf(
                    '.',
                    [StringComparison]::Ordinal
                )
                $decimalPlaces = if ($decimalPoint -lt 0) {
                    0
                } else {
                    $percentText.Length - $decimalPoint - 1
                }
                if (
                    -not [int]::TryParse(
                        $match.Groups['covered'].Value,
                        [ref]$coveredConditions
                    ) -or
                    -not [int]::TryParse(
                        $match.Groups['total'].Value,
                        [ref]$totalConditions
                    ) -or
                    -not [decimal]::TryParse(
                        $percentText,
                        [Globalization.NumberStyles]::AllowDecimalPoint,
                        [Globalization.CultureInfo]::InvariantCulture,
                        [ref]$reportedPercent
                    ) -or
                    $decimalPlaces -gt 28 -or
                    $totalConditions -le 0 -or
                    $coveredConditions -lt 0 -or
                    $coveredConditions -gt $totalConditions
                ) {
                    throw (
                        "Cobertura report '$CoberturaPath' has invalid condition coverage " +
                        "for '$lineKey'."
                    )
                }

                $calculatedPercent =
                    ([decimal]100 * $coveredConditions) / $totalConditions
                $expectedPercent = [Math]::Round(
                    $calculatedPercent,
                    $decimalPlaces,
                    [MidpointRounding]::AwayFromZero
                )
                if ($reportedPercent -ne $expectedPercent) {
                    throw (
                        "Cobertura report '$CoberturaPath' has inconsistent condition coverage " +
                        "for '$lineKey': reported $percentText%, expected $expectedPercent% " +
                        "from $coveredConditions/$totalConditions conditions."
                    )
                }

                if ($branches.ContainsKey($lineKey)) {
                    $existing = $branches[$lineKey]
                    if ($existing.Total -ne $totalConditions) {
                        throw (
                            "Cobertura report '$CoberturaPath' has conflicting condition totals " +
                            "for '$lineKey'."
                        )
                    }
                    $existing.Covered = [Math]::Max($existing.Covered, $coveredConditions)
                } else {
                    $branches[$lineKey] = [pscustomobject]@{
                        Covered = $coveredConditions
                        Total = $totalConditions
                    }
                }
            }
        }
    }

    if ($lines.Count -eq 0) {
        throw 'Cobertura reports contain no production C# lines.'
    }
    if ($branches.Count -eq 0) {
        throw 'Cobertura report contains no production C# branch condition totals.'
    }
    if ($functions.Count -eq 0) {
        throw 'Cobertura report contains no production C# methods.'
    }

    $fileRows = foreach ($group in $lines.Values | Group-Object File) {
        $fileLines = @($group.Group)
        [pscustomobject]@{
            File = $group.Name
            LinePercent = Get-Percentage `
                -Covered @($fileLines | Where-Object Covered).Count `
                -Total $fileLines.Count
        }
    }

    return [pscustomobject]@{
        Lines = [pscustomobject]@{
            Covered = @($lines.Values | Where-Object Covered).Count
            Total = $lines.Count
        }
        Branches = [pscustomobject]@{
            Covered = @($branches.Values | Measure-Object Covered -Sum).Sum
            Total = @($branches.Values | Measure-Object Total -Sum).Sum
        }
        Functions = [pscustomobject]@{
            Covered = @($functions.Values | Where-Object { $_ }).Count
            Total = $functions.Count
        }
        Files = @($fileRows)
    }
}

function Get-FrontendCoverage {
    param(
        [Parameter(Mandatory)]
        [string] $FrontendSummaryPath,

        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    if (-not (Test-Path -LiteralPath $FrontendSummaryPath -PathType Leaf)) {
        throw "Frontend coverage summary '$FrontendSummaryPath' does not exist."
    }

    try {
        $summary = Get-Content -LiteralPath $FrontendSummaryPath -Raw -Encoding utf8 |
            ConvertFrom-Json -Depth 20
    } catch {
        throw "Frontend coverage summary '$FrontendSummaryPath' is malformed: $($_.Exception.Message)"
    }

    $totalProperty = $summary.PSObject.Properties['total']
    if ($null -eq $totalProperty) {
        throw "Frontend coverage summary '$FrontendSummaryPath' is missing 'total'."
    }

    $lines = Get-RequiredMetric -Container $totalProperty.Value -Name lines -Context 'Frontend total'
    $branches = Get-RequiredMetric -Container $totalProperty.Value -Name branches -Context 'Frontend total'
    $functions = Get-RequiredMetric -Container $totalProperty.Value -Name functions -Context 'Frontend total'
    [void](Get-RequiredMetric -Container $totalProperty.Value -Name statements -Context 'Frontend total')

    $files = foreach ($property in $summary.PSObject.Properties) {
        if ($property.Name -ceq 'total') {
            continue
        }

        $file = ConvertTo-RepositoryPath -Path $property.Name -RepositoryRoot $RepositoryRoot
        if (-not (Test-FrontendProductionPath -Path $file)) {
            throw "Frontend coverage contains unexpected production path '$file'."
        }

        $fileLines = Get-RequiredMetric `
            -Container $property.Value `
            -Name lines `
            -Context "Frontend file '$file'"
        [void](Get-RequiredMetric -Container $property.Value -Name branches -Context "Frontend file '$file'")
        [void](Get-RequiredMetric -Container $property.Value -Name functions -Context "Frontend file '$file'")
        [void](Get-RequiredMetric -Container $property.Value -Name statements -Context "Frontend file '$file'")

        [pscustomobject]@{
            File = $file
            LinePercent = $fileLines.Percent
        }
    }

    if (@($files).Count -eq 0) {
        throw 'Frontend coverage summary contains no production files.'
    }

    return [pscustomobject]@{
        Lines = $lines
        Branches = $branches
        Functions = $functions
        Files = @($files)
    }
}

function Get-ChangedProductionFiles {
    param([Parameter(Mandatory)][string] $ChangedFilesPath)

    if (-not (Test-Path -LiteralPath $ChangedFilesPath -PathType Leaf)) {
        throw "Changed-file data '$ChangedFilesPath' does not exist."
    }

    try {
        $changedFiles = @(
            Get-Content -LiteralPath $ChangedFilesPath -Raw -Encoding utf8 |
                ConvertFrom-Json -Depth 5
        )
    } catch {
        throw "Changed-file data '$ChangedFilesPath' is malformed: $($_.Exception.Message)"
    }

    foreach ($path in $changedFiles) {
        if ($path -isnot [string] -or [string]::IsNullOrWhiteSpace($path)) {
            throw "Changed-file data '$ChangedFilesPath' contains a malformed path."
        }
    }

    return @(
        $changedFiles |
            ForEach-Object { $_.Replace('\', '/').TrimStart('./') } |
            Where-Object {
                (Test-CSharpProductionPath -Path $_) -or
                (Test-FrontendProductionPath -Path $_)
            } |
            Sort-Object -Unique
    )
}

function Add-FileTable {
    param(
        [Parameter(Mandatory)]
        [Text.StringBuilder] $Builder,

        [Parameter(Mandatory)]
        [string] $Heading,

        [Parameter(Mandatory)]
        [object[]] $Files
    )

    [void]$Builder.AppendLine("### $Heading")
    [void]$Builder.AppendLine()
    [void]$Builder.AppendLine('| File | Line coverage |')
    [void]$Builder.AppendLine('|---|---:|')
    foreach ($file in $Files) {
        [void]$Builder.AppendLine(
            "| ``$(Escape-MarkdownCell $file.File)`` | $(Format-Percentage $file.LinePercent) |"
        )
    }
    [void]$Builder.AppendLine()
}

function New-CoverageReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $CoberturaPath,

        [Parameter(Mandatory)]
        [string] $FrontendSummaryPath,

        [Parameter(Mandatory)]
        [string] $ChangedFilesPath,

        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [ValidatePattern('^https://github\.com/[^/]+/[^/]+/actions/runs/\d+$')]
        [string] $RunUrl
    )

    $changedFiles = @(Get-ChangedProductionFiles -ChangedFilesPath $ChangedFilesPath)
    $csharp = Get-CSharpCoverage `
        -CoberturaPath $CoberturaPath `
        -RepositoryRoot $RepositoryRoot
    $frontend = Get-FrontendCoverage `
        -FrontendSummaryPath $FrontendSummaryPath `
        -RepositoryRoot $RepositoryRoot

    $builder = [Text.StringBuilder]::new()
    [void]$builder.AppendLine('## Code coverage')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine(
        "Coverage is visibility, not a threshold or proof of behavioral correctness. [View workflow run]($RunUrl)."
    )
    [void]$builder.AppendLine(
        'C# coverage uses condition and method totals from the merged Coverlet Cobertura report.'
    )
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('| Scope | Lines | Branches | Functions |')
    [void]$builder.AppendLine('|---|---:|---:|---:|')
    [void]$builder.AppendLine(
        "| C# | $(Format-Percentage (Get-Percentage $csharp.Lines.Covered $csharp.Lines.Total)) | " +
        "$(Format-Percentage (Get-Percentage $csharp.Branches.Covered $csharp.Branches.Total)) | " +
        "$(Format-Percentage (Get-Percentage $csharp.Functions.Covered $csharp.Functions.Total)) |"
    )
    [void]$builder.AppendLine(
        "| Frontend | $(Format-Percentage $frontend.Lines.Percent) | " +
        "$(Format-Percentage $frontend.Branches.Percent) | " +
        "$(Format-Percentage $frontend.Functions.Percent) |"
    )
    [void]$builder.AppendLine()

    Add-FileTable `
        -Builder $builder `
        -Heading 'Lowest-covered C# files' `
        -Files @($csharp.Files | Sort-Object LinePercent, File | Select-Object -First 10)
    Add-FileTable `
        -Builder $builder `
        -Heading 'Lowest-covered frontend files' `
        -Files @($frontend.Files | Sort-Object LinePercent, File | Select-Object -First 10)

    [void]$builder.AppendLine('### Changed production files')
    [void]$builder.AppendLine()
    if ($changedFiles.Count -eq 0) {
        [void]$builder.AppendLine('No changed production C# or frontend files were reported.')
    } else {
        $coverageByFile = @{}
        foreach ($file in @($csharp.Files) + @($frontend.Files)) {
            $coverageByFile[$file.File] = $file.LinePercent
        }

        [void]$builder.AppendLine('| File | Line coverage |')
        [void]$builder.AppendLine('|---|---:|')
        foreach ($path in $changedFiles | Select-Object -First 50) {
            $coverage = if ($coverageByFile.ContainsKey($path)) {
                Format-Percentage $coverageByFile[$path]
            } else {
                'Not measured'
            }
            [void]$builder.AppendLine("| ``$(Escape-MarkdownCell $path)`` | $coverage |")
        }
        if ($changedFiles.Count -gt 50) {
            [void]$builder.AppendLine()
            [void]$builder.AppendLine("$($changedFiles.Count - 50) additional changed production files omitted.")
        }
    }

    $report = $builder.ToString().TrimEnd()
    if ($report.Length -ge 60000) {
        throw 'Coverage report exceeded the bounded workflow-output size.'
    }

    return $report
}

Export-ModuleMember -Function New-CoverageReport
