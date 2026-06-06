$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj"

$output = & dotnet run --project $project -- --test-augment-series-migration 2>&1
$exitCode = $LASTEXITCODE
$renderedOutput = $output -join [Environment]::NewLine

if ($exitCode -ne 0) {
    throw "Expected augment series migration self-test to pass, but got exit code $exitCode.`n$renderedOutput"
}

if ($renderedOutput -notmatch "SUPABASE_AUGMENT_SERIES_MIGRATION_TEST=PASS") {
    throw "Expected the augment series migration success marker."
}

Write-Host "SUPABASE_AUGMENT_SERIES_MIGRATION_SCRIPT_TEST=PASS"
