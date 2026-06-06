$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj"
$runner = Join-Path $repoRoot "Tools\RunSupabaseStagingMigration.ps1"
$originalSqlServer = [Environment]::GetEnvironmentVariable("MIGRATION_SQLSERVER_CONNECTION")
$originalPostgres = [Environment]::GetEnvironmentVariable("MIGRATION_POSTGRES_CONNECTION")

try {
    $credentialKey = "Pass" + "word"
    $fakeCredential = "not-a-real-" + "credential"
    $env:MIGRATION_SQLSERVER_CONNECTION = ""
    $env:MIGRATION_POSTGRES_CONNECTION = "Host=does-not-exist.invalid;Port=5432;Database=postgres;Username=postgres.example;$credentialKey=$fakeCredential;SSL Mode=Require"

    $output = & dotnet run --project $project -- --initialize-schema 2>&1
    $exitCode = $LASTEXITCODE
    $renderedOutput = $output -join [Environment]::NewLine

    if ($exitCode -ne 2) {
        throw "Expected unconfirmed schema initialization to exit with code 2, but got $exitCode."
    }

    if ($renderedOutput -notmatch "Schema initialization requires --confirm-staging") {
        throw "Expected schema initialization to require explicit staging confirmation."
    }

    if ($renderedOutput -notmatch "RESULT=No schema changes were written\.") {
        throw "Expected the schema initialization no-write safety result."
    }

    $runnerOutput = & powershell -ExecutionPolicy Bypass -File $runner -InitializeSchema 2>&1
    $runnerExitCode = $LASTEXITCODE
    $renderedRunnerOutput = $runnerOutput -join [Environment]::NewLine

    if ($runnerExitCode -ne 2) {
        throw "Expected unconfirmed schema runner to exit with code 2, but got $runnerExitCode."
    }

    if ($renderedRunnerOutput -notmatch "schema initialization requires -ConfirmStaging") {
        throw "Expected the schema runner to require explicit staging confirmation."
    }

    if ($renderedRunnerOutput -notmatch "RESULT=No schema changes were written\.") {
        throw "Expected the schema runner no-write safety result."
    }

    Write-Host "SUPABASE_SCHEMA_INITIALIZATION_SAFETY_TEST=PASS"
}
finally {
    [Environment]::SetEnvironmentVariable("MIGRATION_SQLSERVER_CONNECTION", $originalSqlServer)
    [Environment]::SetEnvironmentVariable("MIGRATION_POSTGRES_CONNECTION", $originalPostgres)
}
