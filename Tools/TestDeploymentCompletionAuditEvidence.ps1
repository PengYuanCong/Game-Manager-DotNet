$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$auditScript = Join-Path $repoRoot "Tools\DeploymentCompletionAudit.ps1"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "proposal-deployment-audit-test"
$migrationReport = Join-Path $tempRoot "migration.json"
$auditReport = Join-Path $tempRoot "audit.json"

$environmentNames = @(
    "MIGRATION_SQLSERVER_CONNECTION",
    "MIGRATION_POSTGRES_CONNECTION",
    "RENDER_PREVIEW_URL",
    "CLOUD_PREVIEW_URL",
    "AZURE_PREVIEW_URL"
)
$originalEnvironment = @{}

foreach ($name in $environmentNames) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    @{
        status = "APPLY_PASS"
        mode = "APPLY"
        utcTimestamp = [DateTimeOffset]::UtcNow.ToString("O")
        secrets = "not recorded"
    } | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $migrationReport

    $output = & powershell -ExecutionPolicy Bypass -File $auditScript `
        -SkipNetworkChecks `
        -SkipLocalSmoke `
        -MigrationReportPath $migrationReport `
        -ReportPath $auditReport 2>&1
    $exitCode = $LASTEXITCODE
    $renderedOutput = $output -join [Environment]::NewLine

    if ($exitCode -ne 2) {
        throw "Expected an external-blocker exit code 2, but got $exitCode.`n$renderedOutput"
    }

    if ($renderedOutput -match "missing environment variable: MIGRATION_") {
        throw "Valid APPLY_PASS evidence must not require migration secrets to remain in the environment.`n$renderedOutput"
    }

    if ($renderedOutput -notmatch "Supabase apply evidence found") {
        throw "Expected valid Supabase apply evidence to be accepted."
    }

    if ($renderedOutput -notmatch "Render preview URL is missing") {
        throw "Expected the Render preview URL to remain the only external deployment blocker."
    }

    Write-Host "DEPLOYMENT_COMPLETION_AUDIT_EVIDENCE_TEST=PASS"
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name])
    }

    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
