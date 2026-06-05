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

function Get-PostgresConnectionMetadata {
    param([string]$ConnectionString)

    if ($ConnectionString -match "^(postgres|postgresql)://") {
        try {
            $uri = [Uri]$ConnectionString
            $userInfo = $uri.UserInfo -split ":", 2

            return [ordered]@{
                Host = $uri.Host
                Port = $uri.Port
                Username = if ($userInfo.Count -gt 0) { [Uri]::UnescapeDataString($userInfo[0]) } else { "" }
                PasswordPresent = $userInfo.Count -gt 1 -and
                    -not [string]::IsNullOrWhiteSpace([Uri]::UnescapeDataString($userInfo[1]))
                PasswordIsPlaceholder = $userInfo.Count -gt 1 -and
                    [Uri]::UnescapeDataString($userInfo[1]) -match "YOUR-PASSWORD|你的.*密碼"
            }
        } catch {
            throw "PostgreSQL connection URI format is invalid."
        }
    }

    function Get-ConnectionValue {
        param(
            [string[]]$Keys,
            [string]$DefaultValue = ""
        )

        foreach ($key in $Keys) {
            $escapedKey = [Regex]::Escape($key)
            $match = [Regex]::Match(
                $ConnectionString,
                "(?i)(?:^|;)\s*$escapedKey\s*=\s*(?:""(?<quoted>[^""]*)""|'(?<single>[^']*)'|(?<plain>[^;]*))"
            )
            if ($match.Success) {
                foreach ($groupName in @("quoted", "single", "plain")) {
                    if ($match.Groups[$groupName].Success) {
                        return $match.Groups[$groupName].Value.Trim()
                    }
                }
            }
        }

        return $DefaultValue
    }

    if ($ConnectionString -notmatch "=") {
        throw "PostgreSQL connection string format is invalid."
    }

    $postgresHost = Get-ConnectionValue @("Host", "Server")
    $port = Get-ConnectionValue @("Port") "5432"
    $username = Get-ConnectionValue @("Username", "User ID", "UserID")
    $credentialSecret = Get-ConnectionValue @("Password", "Pwd")

    return [ordered]@{
        Host = $postgresHost
        Port = $port
        Username = $username
        PasswordPresent = -not [string]::IsNullOrWhiteSpace($credentialSecret)
        PasswordIsPlaceholder = $credentialSecret -match "YOUR-PASSWORD|你的.*密碼"
    }
}

function Assert-PostgresConnectionPreflight {
    $connectionString = [Environment]::GetEnvironmentVariable("MIGRATION_POSTGRES_CONNECTION")

    try {
        $metadata = Get-PostgresConnectionMetadata $connectionString
    } catch {
        Write-Status "FAIL" $_.Exception.Message
        Write-Host "RESULT=No data was written."
        exit 2
    }

    if ([string]::IsNullOrWhiteSpace($metadata.Host)) {
        Write-Status "FAIL" "PostgreSQL host is missing from MIGRATION_POSTGRES_CONNECTION."
        Write-Host "RESULT=No data was written."
        exit 2
    }

    if ($metadata.Host -match "YOUR|你的|example|<|>|\[|\]") {
        Write-Status "FAIL" "PostgreSQL host is still a placeholder: $($metadata.Host)"
        Write-Host "Use the exact Host shown by Supabase Connect > Session pooler."
        Write-Host "RESULT=No data was written."
        exit 2
    }

    if ([string]::IsNullOrWhiteSpace($metadata.Username)) {
        Write-Status "FAIL" "PostgreSQL username is missing from MIGRATION_POSTGRES_CONNECTION."
        Write-Host "RESULT=No data was written."
        exit 2
    }

    if (-not $metadata.PasswordPresent -or $metadata.PasswordIsPlaceholder) {
        Write-Status "FAIL" "PostgreSQL database password is missing or still uses [YOUR-PASSWORD]."
        Write-Host "RESULT=No data was written."
        exit 2
    }

    try {
        $addresses = [System.Net.Dns]::GetHostAddresses($metadata.Host)
        if ($addresses.Count -eq 0) {
            throw "No DNS addresses returned."
        }

        $addressFamilies = @(
            $addresses |
                ForEach-Object { $_.AddressFamily.ToString() } |
                Sort-Object -Unique
        )
        Write-Status "PASS" "PostgreSQL host resolved: $($metadata.Host):$($metadata.Port) [$($addressFamilies -join ', ')]"
    } catch {
        Write-Status "FAIL" "PostgreSQL host could not be resolved: $($metadata.Host)"
        Write-Host "Copy the exact Session pooler Host from Supabase Connect; do not use example text."
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
Assert-PostgresConnectionPreflight

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
