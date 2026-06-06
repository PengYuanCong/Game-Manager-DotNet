param(
    [ValidateSet("Generic", "Render", "Azure")]
    [string]$Target = "Generic",

    [string]$BaseUrl = "",

    [switch]$SkipReadiness,

    [string]$ExpectedReadyProvider = "Supabase",

    [switch]$AllowHttp
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Write-Status {
    param(
        [string]$Status,
        [string]$Message
    )

    Write-Host "[$Status] $Message"
}

function Get-PreviewUrlFromEnvironment {
    param([string]$TargetName)

    $candidateNames = switch ($TargetName) {
        "Render" { @("RENDER_PREVIEW_URL", "CLOUD_PREVIEW_URL") }
        "Azure" { @("AZURE_PREVIEW_URL", "CLOUD_PREVIEW_URL") }
        default { @("CLOUD_PREVIEW_URL", "RENDER_PREVIEW_URL", "AZURE_PREVIEW_URL") }
    }

    foreach ($name in $candidateNames) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            Write-Status "PASS" "using preview URL from environment: $name"
            return $value
        }
    }

    return ""
}

function Test-PreviewUrl {
    param([string]$Url)

    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri)) {
        Write-Status "FAIL" "preview URL is not an absolute URL"
        Write-Host "RESULT=No network call was made."
        exit 2
    }

    if ($uri.Scheme -notin @("http", "https")) {
        Write-Status "FAIL" "preview URL must use http or https"
        Write-Host "RESULT=No network call was made."
        exit 2
    }

    $isLocalhost = $uri.Host -in @("localhost", "127.0.0.1", "::1")
    if ($uri.Scheme -ne "https" -and -not $AllowHttp -and -not $isLocalhost) {
        Write-Status "FAIL" "cloud preview URL must use https. Use -AllowHttp only for local diagnostics."
        Write-Host "RESULT=No network call was made."
        exit 2
    }

    return $uri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
}

Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    $BaseUrl = Get-PreviewUrlFromEnvironment $Target
}

Write-Host "Cloud preview smoke"
Write-Host "Target: $Target"

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    Write-Status "FAIL" "missing preview URL. Set CLOUD_PREVIEW_URL, RENDER_PREVIEW_URL, AZURE_PREVIEW_URL, or pass -BaseUrl."
    Write-Host "RESULT=No network call was made."
    exit 2
}

$normalizedBaseUrl = Test-PreviewUrl $BaseUrl
Write-Host "BaseUrl: $normalizedBaseUrl"
Write-Host "Readiness: $(if ($SkipReadiness) { 'skipped' } else { 'required' })"
if (-not $SkipReadiness) {
    Write-Host "ExpectedReadyProvider: $ExpectedReadyProvider"
}
Write-Host ""

$args = @(
    "-ExecutionPolicy", "Bypass",
    "-File", "Tools\HttpSmokeCheck.ps1",
    "-BaseUrl", $normalizedBaseUrl
)

if (-not $SkipReadiness) {
    $args += "-CheckReadiness"
    if (-not [string]::IsNullOrWhiteSpace($ExpectedReadyProvider)) {
        $args += "-ExpectedReadyProvider"
        $args += $ExpectedReadyProvider
    }
}

& powershell @args
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Status "FAIL" "cloud preview smoke failed"
    exit $exitCode
}

Write-Host "CLOUD_PREVIEW_SMOKE_STATUS=PASS"
