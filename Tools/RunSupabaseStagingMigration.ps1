param(
    [switch]$Apply,

    [switch]$ConfirmStaging,

    [string]$Tables = "",

    [switch]$SkipContractCheck,

    [string]$ReportPath = "Reports\supabase-staging-migration-last.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$migratorProject = Join-Path $repoRoot "Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj"

function Write-Status {
    param(
        [string]$Status,
        [string]$Message
    )

    Write-Host "[$Status] $Message"
}

function Get-EnvIsTruthy {
    param([string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    return $value -match "^(1|true|yes)$"
}

function Assert-RequiredEnvironment {
    $required = @(
        "MIGRATION_SQLSERVER_CONNECTION",
        "MIGRATION_POSTGRES_CONNECTION"
    )

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($name in $required) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            $missing.Add($name) | Out-Null
        } else {
            Write-Status "PASS" "environment variable is set: $name"
        }
    }

    if ($missing.Count -gt 0) {
        Write-Status "FAIL" "missing migration environment variables: $($missing -join ', ')"
        Write-Host "RESULT=No data was written."
        exit 2
    }
}

function Write-MigrationReport {
    param(
        [string]$Status,
        [string]$Mode,
        [string]$SelectedTables
    )

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
        mode = $Mode
        utcTimestamp = [DateTimeOffset]::UtcNow.ToString("O")
        tables = if ([string]::IsNullOrWhiteSpace($SelectedTables)) { "all" } else { $SelectedTables }
        source = "SQL Server"
        target = "PostgreSQL/Supabase staging"
        secrets = "not recorded"
    }

    $report | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -Path $fullReportPath
    Write-Status "PASS" "migration evidence report written: $ReportPath"
}

Set-Location $repoRoot

Write-Host "Supabase staging migration runner"
Write-Host "Mode: $(if ($Apply) { 'APPLY' } else { 'DRY_RUN' })"
Write-Host ""

if ($Apply -and -not $ConfirmStaging -and -not (Get-EnvIsTruthy "MIGRATION_CONFIRM_STAGING")) {
    Write-Status "FAIL" "apply mode requires -ConfirmStaging or MIGRATION_CONFIRM_STAGING=true"
    Write-Host "RESULT=No data was written."
    exit 2
}

Assert-RequiredEnvironment

if (-not $SkipContractCheck) {
    Write-Status "RUN" "Supabase schema contract check"
    & powershell -ExecutionPolicy Bypass -File Tools\SupabaseContractCheck.ps1
    if ($LASTEXITCODE -ne 0) {
        Write-Status "FAIL" "Supabase schema contract check failed"
        Write-Host "RESULT=No data was written."
        exit $LASTEXITCODE
    }
}

Write-Status "RUN" "Supabase migrator build"
dotnet build $migratorProject --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Status "FAIL" "Supabase migrator build failed"
    Write-Host "RESULT=No data was written."
    exit $LASTEXITCODE
}

$migratorArgs = New-Object System.Collections.Generic.List[string]
if ($Apply) {
    $migratorArgs.Add("--apply") | Out-Null
    if ($ConfirmStaging) {
        $migratorArgs.Add("--confirm-staging") | Out-Null
    }
}

if (-not [string]::IsNullOrWhiteSpace($Tables)) {
    $migratorArgs.Add("--tables") | Out-Null
    $migratorArgs.Add($Tables) | Out-Null
}

Write-Status "RUN" "Supabase data migrator"
$migratorArgArray = @($migratorArgs.ToArray())
& dotnet "run" "--project" $migratorProject "--" @migratorArgArray
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Status "FAIL" "Supabase data migrator failed with exit code $exitCode"
    exit $exitCode
}

if ($Apply) {
    Write-MigrationReport "APPLY_PASS" "APPLY" $Tables
    Write-Host "SUPABASE_STAGING_MIGRATION_STATUS=APPLY_PASS"
} else {
    Write-MigrationReport "DRY_RUN_PASS" "DRY_RUN" $Tables
    Write-Host "SUPABASE_STAGING_MIGRATION_STATUS=DRY_RUN_PASS"
}
