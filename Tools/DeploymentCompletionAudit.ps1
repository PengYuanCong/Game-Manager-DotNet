param(
    [switch]$SkipNetworkChecks,

    [switch]$SkipLocalSmoke,

    [switch]$RunRenderPreviewSmoke,

    [switch]$RunAzurePreviewSmoke,

    [string]$MigrationReportPath = "Reports\supabase-staging-migration-last.json",

    [string]$ReportPath = "Reports\deployment-completion-last.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$blockers = New-Object System.Collections.Generic.List[string]
$failures = New-Object System.Collections.Generic.List[string]

function Write-Status {
    param(
        [string]$Status,
        [string]$Message
    )

    Write-Host "[$Status] $Message"
}

function Add-Blocker {
    param([string]$Message)

    $blockers.Add($Message) | Out-Null
    Write-Status "BLOCKED" $Message
}

function Add-Failure {
    param([string]$Message)

    $failures.Add($Message) | Out-Null
    Write-Status "FAIL" $Message
}

function Run-RequiredCommand {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Status "RUN" $Name
    try {
        & $Command
        if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
            throw "exit code $LASTEXITCODE"
        }
        Write-Status "PASS" $Name
    } catch {
        Add-Failure "$Name failed: $($_.Exception.Message)"
    } finally {
        $global:LASTEXITCODE = 0
    }
}

function Get-EnvValue {
    param([string]$Name)
    return [Environment]::GetEnvironmentVariable($Name)
}

function Test-EnvPresent {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace((Get-EnvValue $Name))) {
        Add-Blocker "missing environment variable: $Name"
        return $false
    }

    Write-Status "PASS" "environment variable is set: $Name"
    return $true
}

function Get-PreviewUrl {
    param(
        [string]$PrimaryName,
        [string]$FallbackName
    )

    $value = Get-EnvValue $PrimaryName
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
    }

    return Get-EnvValue $FallbackName
}

function Test-MigrationEvidence {
    $fullReportPath = if ([System.IO.Path]::IsPathRooted($MigrationReportPath)) {
        $MigrationReportPath
    } else {
        Join-Path $repoRoot $MigrationReportPath
    }

    if (-not (Test-Path -LiteralPath $fullReportPath)) {
        Add-Blocker "Supabase apply evidence is missing: $MigrationReportPath"
        return
    }

    try {
        $report = Get-Content -Encoding UTF8 -Path $fullReportPath -Raw | ConvertFrom-Json
    } catch {
        Add-Failure "Supabase migration report is not valid JSON: $MigrationReportPath"
        return
    }

    if ($report.status -eq "APPLY_PASS" -and $report.mode -eq "APPLY") {
        Write-Status "PASS" "Supabase apply evidence found: $MigrationReportPath"
        return
    }

    Add-Blocker "latest Supabase migration evidence is not APPLY_PASS: status=$($report.status); mode=$($report.mode)"
}

function Write-AuditReport {
    param([string]$Status)

    $fullReportPath = if ([System.IO.Path]::IsPathRooted($ReportPath)) {
        $ReportPath
    } else {
        Join-Path $repoRoot $ReportPath
    }

    $reportDirectory = Split-Path -Parent $fullReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report = [ordered]@{
        status = $Status
        utcTimestamp = [DateTimeOffset]::UtcNow.ToString("O")
        failures = @($failures.ToArray())
        blockers = @($blockers.ToArray())
        options = [ordered]@{
            skipNetworkChecks = [bool]$SkipNetworkChecks
            skipLocalSmoke = [bool]$SkipLocalSmoke
            runRenderPreviewSmoke = [bool]$RunRenderPreviewSmoke
            runAzurePreviewSmoke = [bool]$RunAzurePreviewSmoke
        }
        evidence = [ordered]@{
            migrationReportPath = $MigrationReportPath
            secrets = "not recorded"
        }
    }

    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -Path $fullReportPath
    Write-Status "PASS" "deployment audit report written: $ReportPath"
}

Set-Location $repoRoot

Write-Host "Deployment completion audit"
Write-Host "Repo: $repoRoot"
Write-Host ""

Run-RequiredCommand "cloud readiness gate" {
    if ($SkipNetworkChecks) {
        powershell -ExecutionPolicy Bypass -File Tools\CloudReadinessCheck.ps1 -SkipNetworkChecks
    } else {
        powershell -ExecutionPolicy Bypass -File Tools\CloudReadinessCheck.ps1
    }
}

if (-not $SkipLocalSmoke) {
    Run-RequiredCommand "local smoke gate" {
        powershell -ExecutionPolicy Bypass -File Tools\RunLocalSmoke.ps1 -Port 5226
    }
}

Test-EnvPresent "MIGRATION_SQLSERVER_CONNECTION" | Out-Null
Test-EnvPresent "MIGRATION_POSTGRES_CONNECTION" | Out-Null
Test-MigrationEvidence

$renderUrl = Get-PreviewUrl "RENDER_PREVIEW_URL" "CLOUD_PREVIEW_URL"
if ([string]::IsNullOrWhiteSpace($renderUrl)) {
    Add-Blocker "Render preview URL is missing: set RENDER_PREVIEW_URL or CLOUD_PREVIEW_URL"
} elseif ($RunRenderPreviewSmoke) {
    Run-RequiredCommand "Render preview smoke" {
        powershell -ExecutionPolicy Bypass -File Tools\RunCloudPreviewSmoke.ps1 -Target Render -BaseUrl $renderUrl
    }
} else {
    Add-Blocker "Render preview URL exists but smoke was not run in this audit; re-run with -RunRenderPreviewSmoke"
}

$azureUrl = Get-PreviewUrl "AZURE_PREVIEW_URL" "CLOUD_PREVIEW_URL"
if (-not [string]::IsNullOrWhiteSpace($azureUrl)) {
    if ($RunAzurePreviewSmoke) {
        Run-RequiredCommand "Azure preview smoke" {
            powershell -ExecutionPolicy Bypass -File Tools\RunCloudPreviewSmoke.ps1 -Target Azure -BaseUrl $azureUrl
        }
    } else {
        Add-Blocker "Azure preview URL exists but smoke was not run in this audit; re-run with -RunAzurePreviewSmoke"
    }
} else {
    Write-Status "INFO" "Azure preview URL is not required for pre-Azure static readiness; AzurePreflightCheck covers current scope."
}

Write-Host ""
Write-Host "Summary"
Write-Host "Failures: $($failures.Count)"
Write-Host "Blockers: $($blockers.Count)"

foreach ($failure in $failures) {
    Write-Host "FAIL: $failure"
}

foreach ($blocker in $blockers) {
    Write-Host "BLOCKED: $blocker"
}

if ($failures.Count -gt 0) {
    Write-Host "DEPLOYMENT_COMPLETION_STATUS=FAILED"
    Write-AuditReport "FAILED"
    exit 1
}

if ($blockers.Count -gt 0) {
    Write-Host "DEPLOYMENT_COMPLETION_STATUS=INCOMPLETE_EXTERNAL_BLOCKERS"
    Write-AuditReport "INCOMPLETE_EXTERNAL_BLOCKERS"
    exit 2
}

Write-Host "DEPLOYMENT_COMPLETION_STATUS=READY"
Write-AuditReport "READY"
