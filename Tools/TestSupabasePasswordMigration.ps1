$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj"

$output = & dotnet run --project $project -- --test-password-migration 2>&1
$exitCode = $LASTEXITCODE
$renderedOutput = $output -join [Environment]::NewLine

if ($exitCode -ne 0) {
    throw "Expected password migration self-test to pass, but got exit code $exitCode.`n$renderedOutput"
}

if ($renderedOutput -notmatch "SUPABASE_PASSWORD_MIGRATION_TEST=PASS") {
    throw "Expected the password migration success marker."
}

Write-Host "SUPABASE_PASSWORD_MIGRATION_SCRIPT_TEST=PASS"
