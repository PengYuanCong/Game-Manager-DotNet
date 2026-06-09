param(
    [string]$SolutionPath = "Proposal.slnx"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$fullPath = if ([System.IO.Path]::IsPathRooted($SolutionPath)) {
    $SolutionPath
} else {
    Join-Path $repoRoot $SolutionPath
}

Write-Host "NuGet vulnerability scan"
Write-Host "Target: $fullPath"
Write-Host ""

Push-Location $repoRoot
try {
    $outputLines = @(
        dotnet list $fullPath package --vulnerable --include-transitive --format json
    )
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet list package failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

$jsonText = $outputLines -join [Environment]::NewLine
try {
    $report = $jsonText | ConvertFrom-Json
} catch {
    Write-Host $jsonText
    throw "NuGet vulnerability output was not valid JSON: $($_.Exception.Message)"
}

$findings = New-Object System.Collections.Generic.List[string]
foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks)) {
        if ($null -eq $framework) {
            continue
        }

        foreach ($collectionName in @("topLevelPackages", "transitivePackages")) {
            foreach ($package in @($framework.$collectionName)) {
                if ($null -eq $package) {
                    continue
                }

                $vulnerabilities = @(
                    $package.vulnerabilities |
                        Where-Object { $null -ne $_ }
                )
                if ($vulnerabilities.Count -eq 0) {
                    continue
                }

                foreach ($vulnerability in $vulnerabilities) {
                    $findings.Add(
                        "$($project.path): $($package.id) $($package.resolvedVersion) " +
                        "severity=$($vulnerability.severity) advisory=$($vulnerability.advisoryUrl)"
                    ) | Out-Null
                }
            }
        }
    }
}

Write-Host "PROJECTS=$(@($report.projects).Count)"
Write-Host "VULNERABILITIES=$($findings.Count)"

if ($findings.Count -gt 0) {
    foreach ($finding in $findings) {
        Write-Host "[FAIL] $finding"
    }

    Write-Host "NUGET_VULNERABILITY_STATUS=FAIL"
    exit 1
}

Write-Host "NUGET_VULNERABILITY_STATUS=PASS"
