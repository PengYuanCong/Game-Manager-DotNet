param(
    [string]$ProjectPath = "Proposal"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFullPath = Join-Path $repoRoot $ProjectPath
$failures = New-Object System.Collections.Generic.List[string]

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

function Get-RelativePath {
    param([string]$Path)

    if ($Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring($repoRoot.Length).TrimStart("\")
    }

    return $Path
}

function Require-File {
    param([string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (Test-Path -LiteralPath $path) {
        Write-Check "PASS" "file exists" $RelativePath
        return $path
    }

    Add-Failure "missing required file: $RelativePath"
    return $null
}

function Require-Text {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Name
    )

    if ($Text -match $Pattern) {
        Write-Check "PASS" $Name
    } else {
        Add-Failure "missing required code/config marker: $Name"
    }
}

function Test-NoDirectDataAccessInUiLayer {
    $paths = @(
        (Join-Path $projectFullPath "Controllers"),
        (Join-Path $projectFullPath "Views"),
        (Join-Path $projectFullPath "wwwroot")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $bannedPatterns = @(
        "\bnew\s+SqlConnection\b",
        "\bnew\s+NpgsqlConnection\b",
        "\bSqlCommand\b",
        "\bNpgsqlCommand\b",
        "\bSqlDataReader\b",
        "\bGetConnectionString\s*\(",
        "ConnectionStrings__DefaultConnection",
        "Database__Provider"
    )

    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        $files = Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in ".cs", ".cshtml", ".js", ".css" }

        foreach ($file in $files) {
            foreach ($pattern in $bannedPatterns) {
                $hits = Select-String -LiteralPath $file.FullName -Pattern $pattern -AllMatches -ErrorAction SilentlyContinue
                foreach ($hit in $hits) {
                    $findings.Add("$(Get-RelativePath $hit.Path):$($hit.LineNumber) matches $pattern") | Out-Null
                }
            }
        }
    }

    if ($findings.Count -eq 0) {
        Write-Check "PASS" "UI layer has no direct DB connection/command usage"
    } else {
        Add-Failure "UI layer direct data access findings: $($findings -join '; ')"
    }
}

function Test-NoSecretsInClientFiles {
    $paths = @(
        (Join-Path $projectFullPath "Views"),
        (Join-Path $projectFullPath "wwwroot")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $patterns = @(
        "OpenRouter__ApiKey",
        "OpenRouter:ApiKey",
        "YouTubeSettings__ApiKey",
        "service_role",
        "ConnectionStrings__DefaultConnection",
        "\bPassword\s*="
    )

    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        $files = Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in ".cshtml", ".js", ".css", ".html" }

        foreach ($file in $files) {
            foreach ($pattern in $patterns) {
                $hits = Select-String -LiteralPath $file.FullName -Pattern $pattern -AllMatches -ErrorAction SilentlyContinue
                foreach ($hit in $hits) {
                    $findings.Add("$(Get-RelativePath $hit.Path):$($hit.LineNumber) matches $pattern") | Out-Null
                }
            }
        }
    }

    if ($findings.Count -eq 0) {
        Write-Check "PASS" "client-rendered files do not expose server secrets"
    } else {
        Add-Failure "client-rendered secret findings: $($findings -join '; ')"
    }
}

function Test-ProductionSecurityMarkers {
    $programPath = Require-File "$ProjectPath\Program.cs"
    if ($null -eq $programPath) {
        return
    }

    $program = [System.IO.File]::ReadAllText($programPath)
    Require-Text $program "CookieSecurePolicy\.Always" "production cookies require Secure"
    Require-Text $program "Cookie\.HttpOnly\s*=\s*true" "cookies are HttpOnly"
    Require-Text $program "SameSiteMode\.Lax" "cookie SameSite is set"
    Require-Text $program "UseForwardedHeaders\s*\(" "forwarded headers are enabled for reverse proxies"
    Require-Text $program "UseHttpsRedirection\s*\(" "HTTPS redirection is enabled"
    Require-Text $program "UseHsts\s*\(" "HSTS is enabled outside development"
    Require-Text $program "X-Content-Type-Options" "nosniff header is configured"
    Require-Text $program "Referrer-Policy" "referrer policy header is configured"
    Require-Text $program "X-Frame-Options" "frame denial header is configured"
    Require-Text $program 'MapGet\("/healthz"' "health endpoint is mapped"
    Require-Text $program 'MapGet\("/readyz"' "readiness endpoint is mapped"
    Require-Text $program "IsPostgresProvider" "database provider switch exists"
}

Write-Host "Architecture dependency check"
Write-Host "Repo: $repoRoot"
Write-Host ""

if (-not (Test-Path -LiteralPath $projectFullPath -PathType Container)) {
    Add-Failure "missing project directory: $ProjectPath"
} else {
    Test-NoDirectDataAccessInUiLayer
    Test-NoSecretsInClientFiles
    Test-ProductionSecurityMarkers
}

Write-Host ""
Write-Host "Summary"
Write-Host "Failures: $($failures.Count)"

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "FAIL: $failure"
    }

    exit 1
}

Write-Host "ARCHITECTURE_DEPENDENCY_STATUS=PASS"
