param(
    [Parameter(Mandatory = $false)]
    [string]$ResultsDirectory = "TestResults/Coverage"
)

$ErrorActionPreference = "Stop"

$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml")

if ($reports.Count -eq 0) {
    throw "No coverage.cobertura.xml report was found under '$ResultsDirectory'."
}

if ($reports.Count -ne 1) {
    throw "Expected exactly one coverage report under '$ResultsDirectory', but found $($reports.Count)."
}

$report = $reports[0]
[xml]$coverage = Get-Content -LiteralPath $report.FullName
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-FullCoverage {
    param(
        [string]$Name,
        [string]$LineRate,
        [string]$BranchRate
    )

    $line = [double]::Parse($LineRate, [System.Globalization.CultureInfo]::InvariantCulture)
    $branch = [double]::Parse($BranchRate, [System.Globalization.CultureInfo]::InvariantCulture)
    Write-Host ("{0}: lines {1:P2}, branches {2:P2}" -f $Name, $line, $branch)

    if ($line -ne 1.0) {
        $failures.Add("$Name line coverage is $($line.ToString('P2')); expected 100.00%.")
    }

    if ($branch -ne 1.0) {
        $failures.Add("$Name branch coverage is $($branch.ToString('P2')); expected 100.00%.")
    }
}

Assert-FullCoverage `
    -Name "Total" `
    -LineRate $coverage.coverage.'line-rate' `
    -BranchRate $coverage.coverage.'branch-rate'

foreach ($package in $coverage.coverage.packages.package) {
    Assert-FullCoverage `
        -Name $package.name `
        -LineRate $package.'line-rate' `
        -BranchRate $package.'branch-rate'
}

$expectedPackages = @("CrowCast", "CrowCast.Akka")
$actualPackages = @($coverage.coverage.packages.package | ForEach-Object { $_.name })
foreach ($expectedPackage in $expectedPackages) {
    if ($actualPackages -notcontains $expectedPackage) {
        $failures.Add("Coverage report does not contain the required package '$expectedPackage'.")
    }
}

foreach ($actualPackage in $actualPackages) {
    if ($expectedPackages -notcontains $actualPackage) {
        $failures.Add("Coverage report contains unexpected package '$actualPackage'.")
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Coverage gate passed: 100% lines and 100% branches."
