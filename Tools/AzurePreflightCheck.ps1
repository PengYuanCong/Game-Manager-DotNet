param(
    [string]$DockerfilePath = "Dockerfile",
    [string]$DeploymentEnvExamplePath = "deployment.env.example",
    [string]$ProjectPath = "Proposal"
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
        return $path
    }

    Add-Failure "missing required file: $RelativePath"
    return $null
}

function Require-Contains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Name
    )

    if ($Text.Contains($Needle)) {
        Write-Check "PASS" $Name $Needle
    } else {
        Add-Failure "$Name is missing: $Needle"
    }
}

function Read-EnvExample {
    param([string]$Path)

    $values = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=")
        if ($separatorIndex -lt 0) {
            continue
        }

        $name = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        $values[$name] = $value
    }

    return $values
}

function Get-EnvExampleValue {
    param(
        [hashtable]$Values,
        [string]$Name
    )

    if ($Values.ContainsKey($Name)) {
        return [string]$Values[$Name]
    }

    return ""
}

Write-Host "Azure preflight check"
Write-Host "Repo: $repoRoot"
Write-Host ""

$dockerfile = Require-File $DockerfilePath
$envExample = Require-File $DeploymentEnvExamplePath
$programFile = Require-File "$ProjectPath\Program.cs"
$accountController = Require-File "$ProjectPath\Controllers\AccountController.cs"

if ($null -ne $dockerfile) {
    $dockerText = [System.IO.File]::ReadAllText($dockerfile)
    Require-Contains $dockerText "FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime" "runtime image is ASP.NET"
    Require-Contains $dockerText "ENV ASPNETCORE_URLS=http://+:8080" "container listens on port 8080"
    Require-Contains $dockerText "ENV ASPNETCORE_ENVIRONMENT=Production" "container defaults to Production"
    Require-Contains $dockerText "EXPOSE 8080" "container exposes Azure route port"
    Require-Contains $dockerText 'ENTRYPOINT ["dotnet", "Proposal.dll"]' "container entrypoint starts app"
}

if ($null -ne $envExample) {
    $envText = [System.IO.File]::ReadAllText($envExample)
    $envValues = Read-EnvExample $envExample

    foreach ($required in @(
        "ASPNETCORE_ENVIRONMENT",
        "ASPNETCORE_URLS",
        "WEBSITES_PORT",
        "Database__Provider",
        "ConnectionStrings__DefaultConnection",
        "APP_ADMIN_USERS",
        "OpenRouter__ApiKey"
    )) {
        if ($envValues.ContainsKey($required)) {
            Write-Check "PASS" "deployment env example contains" $required
        } else {
            Add-Failure "deployment env example missing: $required"
        }
    }

    if ((Get-EnvExampleValue $envValues "ASPNETCORE_ENVIRONMENT") -eq "Production") {
        Write-Check "PASS" "deployment env example uses Production"
    } else {
        Add-Failure "ASPNETCORE_ENVIRONMENT should be Production for cloud"
    }

    if ((Get-EnvExampleValue $envValues "ASPNETCORE_URLS") -eq "http://+:8080" -and (Get-EnvExampleValue $envValues "WEBSITES_PORT") -eq "8080") {
        Write-Check "PASS" "Azure container port settings align" "ASPNETCORE_URLS + WEBSITES_PORT"
    } else {
        Add-Failure "Azure container port settings must align on 8080"
    }

    if ((Get-EnvExampleValue $envValues "Database__Provider") -eq "Supabase") {
        Write-Check "PASS" "cloud database provider example is Supabase"
    } else {
        Add-Failure "Database__Provider should be Supabase for cloud staging"
    }

    foreach ($secretName in @("ConnectionStrings__DefaultConnection", "APP_ADMIN_USERS", "OpenRouter__ApiKey", "YouTubeSettings__ApiKey")) {
        if ($envValues.ContainsKey($secretName) -and -not [string]::IsNullOrWhiteSpace($envValues[$secretName])) {
            Add-Failure "deployment env example must not contain concrete secret value: $secretName"
        }
    }

    $forbiddenNeedles = @(
        "sk-or-v1-",
        "service_role",
        ("Password" + "="),
        "xoJz"
    )

    foreach ($forbidden in $forbiddenNeedles) {
        if ($envText.Contains($forbidden)) {
            Add-Failure "deployment env example contains forbidden secret-looking text: $forbidden"
        }
    }
}

if ($null -ne $programFile) {
    $programText = [System.IO.File]::ReadAllText($programFile)
    Require-Contains $programText "UseForwardedHeaders" "Azure reverse proxy forwarded headers are handled"
    Require-Contains $programText "UseHttpsRedirection" "HTTPS redirection is configured"
    Require-Contains $programText 'MapGet("/healthz"' "liveness endpoint exists"
    Require-Contains $programText 'MapGet("/readyz"' "database readiness endpoint exists"
}

if ($null -ne $accountController) {
    $accountText = [System.IO.File]::ReadAllText($accountController)
    if ($accountText -match "IsProduction\(\)\s*\?\s*Array\.Empty<string>\(\)") {
        Write-Check "PASS" "production admin fallback is empty"
    } else {
        Add-Failure "production must not fall back to local default admin users"
    }
}

foreach ($name in @(
    "Database__Provider",
    "ConnectionStrings__DefaultConnection",
    "APP_ADMIN_USERS",
    "OpenRouter__ApiKey"
)) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        Add-Warning "Azure app setting is not set locally: $name"
    } else {
        Write-Check "PASS" "Azure app setting present locally" $name
    }
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

Write-Host "AZURE_PREFLIGHT_STATUS=PASS_WITH_WARNINGS_ALLOWED"
