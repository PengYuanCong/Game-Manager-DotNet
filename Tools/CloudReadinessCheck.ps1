param(
    [switch]$SkipNetworkChecks
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Write-Check {
    param(
        [string]$Status,
        [string]$Name,
        [string]$Detail = ""
    )

    $line = "[$Status] $Name"
    if (-not [string]::IsNullOrWhiteSpace($Detail)) {
        $line = "$line - $Detail"
    }

    Write-Host $line
}

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
    Write-Check "FAIL" $Message
}

function Add-Warning {
    param([string]$Message)
    $warnings.Add($Message) | Out-Null
    Write-Check "WARN" $Message
}

function Require-File {
    param([string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (Test-Path -LiteralPath $path) {
        Write-Check "PASS" "file exists" $RelativePath
    } else {
        Add-Failure "missing required file: $RelativePath"
    }
}

function Run-Command {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Check "RUN" $Name
    Push-Location $repoRoot
    try {
        & $Command
        if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
            throw "exit code $LASTEXITCODE"
        }

        Write-Check "PASS" $Name
    } catch {
        Add-Failure "$Name failed: $($_.Exception.Message)"
    } finally {
        Pop-Location
        $global:LASTEXITCODE = 0
    }
}

function Test-RenderYaml {
    $renderPath = Join-Path $repoRoot "render.yaml"
    if (-not (Test-Path -LiteralPath $renderPath)) {
        Add-Failure "render.yaml is missing"
        return
    }

    $text = [System.IO.File]::ReadAllText($renderPath)
    $requiredText = @(
        "runtime: docker",
        "dockerfilePath: ./Dockerfile",
        "healthCheckPath: /healthz",
        "ASPNETCORE_ENVIRONMENT",
        "ASPNETCORE_URLS",
        "Database__Provider",
        "ConnectionStrings__DefaultConnection",
        "OpenRouter__ApiKey",
        "APP_ADMIN_USERS"
    )

    foreach ($needle in $requiredText) {
        if ($text.Contains($needle)) {
            Write-Check "PASS" "render.yaml contains" $needle
        } else {
            Add-Failure "render.yaml missing: $needle"
        }
    }

    foreach ($secretKey in @("ConnectionStrings__DefaultConnection", "OpenRouter__ApiKey", "YouTubeSettings__ApiKey", "APP_ADMIN_USERS")) {
        $pattern = "(?s)- key:\s*$([regex]::Escape($secretKey)).*?sync:\s*false"
        if ($text -match $pattern) {
            Write-Check "PASS" "render secret is sync:false" $secretKey
        } else {
            Add-Failure "render secret must be sync:false: $secretKey"
        }
    }
}

function Test-AntiforgeryCoverage {
    $controllerFiles = Get-ChildItem -Path (Join-Path $repoRoot "Proposal\Controllers") -Filter "*.cs" -File
    $missing = New-Object System.Collections.Generic.List[string]

    foreach ($file in $controllerFiles) {
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match "\[HttpPost") {
                $end = [Math]::Min($i + 5, $lines.Length - 1)
                $window = ($lines[$i..$end] -join " ")
                if ($window -notmatch "ValidateAntiForgeryToken") {
                    $missing.Add("$($file.Name):$($i + 1)") | Out-Null
                }
            }
        }
    }

    if ($missing.Count -eq 0) {
        Write-Check "PASS" "all HttpPost actions have antiforgery"
    } else {
        Add-Failure "missing antiforgery on: $($missing -join ', ')"
    }
}

function Test-HighConfidenceSecrets {
    $paths = @(
        "Proposal",
        "Tools",
        "database",
        "Dockerfile",
        "render.yaml",
        "deployment.env.example",
        "DEPLOYMENT_SECURITY.md",
        "SUPABASE_MIGRATION_STATUS.md",
        "CONTEXT.MD"
    ) | ForEach-Object { Join-Path $repoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ }

    $patterns = @(
        "sk-or-v1-[A-Za-z0-9]{32,}",
        "xoJz[A-Za-z0-9]{20,}",
        "service_role\s*=\s*['""][^'""]+['""]",
        "\bPassword\s*=\s*(?!<)[^;\s]+",
        "SUPABASE_SERVICE_ROLE\s*=\s*(?!<)[^\s]+"
    )

    $secretFindings = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        $files = if (Test-Path -LiteralPath $path -PathType Container) {
            Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch "\\(bin|obj|build-check)\\" }
        } else {
            Get-Item -LiteralPath $path
        }

        foreach ($file in $files) {
            foreach ($pattern in $patterns) {
                $found = Select-String -Path $file.FullName -Pattern $pattern -AllMatches -ErrorAction SilentlyContinue
                foreach ($hit in $found) {
                    if ($hit.Line -match "rg\s+-uuu" -or $hit.Line -match "<[^>]+>") {
                        continue
                    }

                    $relative = $hit.Path
                    if ($relative.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
                        $relative = $relative.Substring($repoRoot.Length).TrimStart("\")
                    }
                    $secretFindings.Add("${relative}:$($hit.LineNumber)") | Out-Null
                }
            }
        }
    }

    if ($secretFindings.Count -eq 0) {
        Write-Check "PASS" "high-confidence secret scan"
    } else {
        Add-Failure "possible leaked secret at: $($secretFindings -join ', ')"
    }
}

function Test-EnvironmentReadiness {
    $requiredForCloudRuntime = @(
        "Database__Provider",
        "ConnectionStrings__DefaultConnection",
        "OpenRouter__ApiKey",
        "APP_ADMIN_USERS"
    )

    foreach ($name in $requiredForCloudRuntime) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            Add-Warning "cloud runtime env var is not set locally: $name"
        } else {
            Write-Check "PASS" "cloud runtime env var present" $name
        }
    }

    $migrationVars = @("MIGRATION_SQLSERVER_CONNECTION", "MIGRATION_POSTGRES_CONNECTION")
    foreach ($name in $migrationVars) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            Add-Warning "migration env var is not set locally: $name"
        } else {
            Write-Check "PASS" "migration env var present" $name
        }
    }

    if (Get-Command docker -ErrorAction SilentlyContinue) {
        Write-Check "PASS" "docker command is available"
    } else {
        Add-Warning "docker command is not available locally; Render Docker smoke must run on Render or a Docker host"
    }
}

function Test-MigratorSafety {
    $migratorPath = Join-Path $repoRoot "Tools\SupabaseDataMigrator\Program.cs"
    if (-not (Test-Path -LiteralPath $migratorPath)) {
        Add-Failure "Supabase migrator Program.cs is missing"
        return
    }

    $text = [System.IO.File]::ReadAllText($migratorPath)
    $markers = @(
        "--confirm-staging",
        "--initialize-schema",
        "MIGRATION_CONFIRM_STAGING",
        "InitializeSchemaAsync",
        "ValidateTargetSchemaAsync",
        "BeginTransactionAsync",
        "RollbackAsync",
        "RlsEnabled"
    )

    foreach ($marker in $markers) {
        if ($text.Contains($marker)) {
            Write-Check "PASS" "Supabase migrator safety marker" $marker
        } else {
            Add-Failure "Supabase migrator missing safety marker: $marker"
        }
    }
}

Set-Location $repoRoot

Write-Host "Cloud readiness check"
Write-Host "Repo: $repoRoot"
Write-Host ""

Require-File "Dockerfile"
Require-File "render.yaml"
Require-File "deployment.env.example"
Require-File "database\supabase\0001_schema.sql"
Require-File "database\supabase\0002_seed_aram_starter_data.sql"
Require-File "Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj"
Require-File "Tools\RunSupabaseStagingMigration.ps1"
Require-File "Tools\TestSupabaseSchemaInitializationSafety.ps1"
Require-File "Tools\SupabaseContractCheck.ps1"
Require-File "Tools\ArchitectureDependencyCheck.ps1"
Require-File "Tools\AzurePreflightCheck.ps1"
Require-File "Tools\HttpSmokeCheck.ps1"
Require-File "Tools\RunLocalSmoke.ps1"
Require-File "Tools\RunCloudPreviewSmoke.ps1"
Require-File "Tools\DeploymentCompletionAudit.ps1"
Require-File "DEPLOYMENT_SECURITY.md"

Test-RenderYaml
Test-AntiforgeryCoverage
Test-HighConfidenceSecrets
Test-EnvironmentReadiness
Test-MigratorSafety

Run-Command "MVC safe build" {
    powershell -ExecutionPolicy Bypass -File Tools\SafeBuild.ps1 -Output build-check\cloud-readiness
}

Run-Command "Supabase schema contract check" {
    powershell -ExecutionPolicy Bypass -File Tools\SupabaseContractCheck.ps1
}

Run-Command "Architecture dependency check" {
    powershell -ExecutionPolicy Bypass -File Tools\ArchitectureDependencyCheck.ps1
}

Run-Command "Azure preflight check" {
    powershell -ExecutionPolicy Bypass -File Tools\AzurePreflightCheck.ps1
}

Run-Command "Supabase migrator build" {
    dotnet build Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj --nologo
}

Run-Command "Supabase schema initialization safety test" {
    powershell -ExecutionPolicy Bypass -File Tools\TestSupabaseSchemaInitializationSafety.ps1
}

Run-Command "Release publish" {
    dotnet publish Proposal\Proposal.csproj --configuration Release --output build-check\cloud-readiness-publish --no-restore -p:UseAppHost=false --nologo
}

if (-not $SkipNetworkChecks) {
    Run-Command "NuGet vulnerability check" {
        dotnet list Proposal\Proposal.csproj package --vulnerable --include-transitive
    }
} else {
    Add-Warning "network checks skipped by caller"
}

Write-Host ""
Write-Host "Summary"
Write-Host "Failures: $($failures.Count)"
Write-Host "Warnings: $($warnings.Count)"

if ($warnings.Count -gt 0) {
    foreach ($warning in $warnings) {
        Write-Host "WARN: $warning"
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "FAIL: $failure"
    }
    exit 1
}

Write-Host "CLOUD_READINESS_STATUS=PASS_WITH_WARNINGS_ALLOWED"
