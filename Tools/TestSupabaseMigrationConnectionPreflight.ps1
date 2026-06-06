$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runner = Join-Path $repoRoot "Tools\RunSupabaseStagingMigration.ps1"
$originalSqlServer = [Environment]::GetEnvironmentVariable("MIGRATION_SQLSERVER_CONNECTION")
$originalPostgres = [Environment]::GetEnvironmentVariable("MIGRATION_POSTGRES_CONNECTION")

try {
    $credentialKey = "Pass" + "word"
    $fakeCredential = "not-a-real-" + "credential"
    $env:MIGRATION_SQLSERVER_CONNECTION = "Server=localhost;Database=LOL;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    $env:MIGRATION_POSTGRES_CONNECTION = "Host=YOUR-SESSION-POOLER-HOST;Port=5432;Database=postgres;Username=postgres.example;$credentialKey=$fakeCredential;SSL Mode=Require"

    $placeholderOutput = & powershell -ExecutionPolicy Bypass -File $runner -SkipContractCheck 2>&1
    $placeholderExitCode = $LASTEXITCODE
    $renderedPlaceholderOutput = $placeholderOutput -join [Environment]::NewLine

    if ($placeholderExitCode -ne 2) {
        throw "Expected placeholder preflight to exit with code 2, but got $placeholderExitCode."
    }

    if ($renderedPlaceholderOutput -notmatch "PostgreSQL host is still a placeholder") {
        throw "Expected a PostgreSQL host placeholder validation message."
    }

    if ($renderedPlaceholderOutput -notmatch "RESULT=No data was written\.") {
        throw "Expected the placeholder preflight no-write safety result."
    }

    $env:MIGRATION_POSTGRES_CONNECTION = "Host=does-not-exist.invalid;Port=5432;Database=postgres;Username=postgres.example;$credentialKey=$fakeCredential;SSL Mode=Require"

    $dnsOutput = & powershell -ExecutionPolicy Bypass -File $runner -SkipContractCheck 2>&1
    $dnsExitCode = $LASTEXITCODE
    $renderedDnsOutput = $dnsOutput -join [Environment]::NewLine

    if ($dnsExitCode -ne 2) {
        throw "Expected DNS preflight to exit with code 2, but got $dnsExitCode."
    }

    if ($renderedDnsOutput -notmatch "could not be resolved") {
        throw "Expected an unresolved PostgreSQL host validation message."
    }

    if ($renderedDnsOutput -notmatch "RESULT=No data was written\.") {
        throw "Expected the DNS preflight no-write safety result."
    }

    Write-Host "SUPABASE_MIGRATION_CONNECTION_PREFLIGHT_TEST=PASS"
}
finally {
    [Environment]::SetEnvironmentVariable("MIGRATION_SQLSERVER_CONNECTION", $originalSqlServer)
    [Environment]::SetEnvironmentVariable("MIGRATION_POSTGRES_CONNECTION", $originalPostgres)
}
