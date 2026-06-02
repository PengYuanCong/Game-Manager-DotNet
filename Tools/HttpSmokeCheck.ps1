param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [int]$TimeoutSeconds = 20,

    [switch]$CheckReadiness,

    [string]$ExpectedReadyProvider = "",

    [switch]$SkipSecurityHeaders
)

$ErrorActionPreference = "Stop"

$failures = New-Object System.Collections.Generic.List[string]

function Join-Url {
    param(
        [string]$Root,
        [string]$Path
    )

    return "$($Root.TrimEnd('/'))/$($Path.TrimStart('/'))"
}

function Add-Failure {
    param([string]$Message)

    $failures.Add($Message) | Out-Null
    Write-Host "[FAIL] $Message"
}

function Get-FinalUrl {
    param([object]$Response, [string]$FallbackUrl)

    if ($Response.BaseResponse -and $Response.BaseResponse.ResponseUri) {
        return $Response.BaseResponse.ResponseUri.AbsoluteUri
    }

    return $FallbackUrl
}

function Get-ResponseHeader {
    param(
        [object]$Response,
        [string]$Name
    )

    foreach ($key in $Response.Headers.Keys) {
        if ([string]::Equals([string]$key, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return (($Response.Headers[$key]) -join ",")
        }
    }

    return ""
}

function Test-SecurityHeaders {
    param(
        [object]$Response,
        [string]$Path
    )

    if ($SkipSecurityHeaders) {
        return
    }

    $requiredHeaders = @{
        "X-Content-Type-Options" = "nosniff"
        "Referrer-Policy" = "strict-origin-when-cross-origin"
        "X-Frame-Options" = "DENY"
    }

    foreach ($name in $requiredHeaders.Keys) {
        $actual = Get-ResponseHeader $Response $name
        if ([string]::IsNullOrWhiteSpace($actual)) {
            Add-Failure "$Path missing security header: $name"
            continue
        }

        if (-not $actual.Equals($requiredHeaders[$name], [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure "$Path security header mismatch: $name expected '$($requiredHeaders[$name])' got '$actual'"
        }
    }
}

function Test-ExceptionContent {
    param(
        [string]$Path,
        [string]$Content
    )

    $markers = @(
        "An unhandled exception occurred",
        "Developer Exception Page",
        "Stack Trace",
        "SqlException",
        "NpgsqlException",
        "System\.InvalidOperationException",
        "ConnectionStrings__DefaultConnection",
        "Data Source=",
        ("Host=.*" + "Pass" + "word=")
    )

    foreach ($marker in $markers) {
        if ($Content -match $marker) {
            Add-Failure "$Path rendered exception or sensitive diagnostic content: $marker"
            return
        }
    }
}

function Invoke-SmokeRequest {
    param([string]$Path)

    $url = Join-Url $BaseUrl $Path
    return Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec $TimeoutSeconds -MaximumRedirection 8
}

function Test-Page {
    param(
        [string]$Path,
        [string]$Expectation = "loads"
    )

    $url = Join-Url $BaseUrl $Path
    try {
        $response = Invoke-SmokeRequest $Path
        $content = [string]$response.Content
        $finalUrl = Get-FinalUrl $response $url

        if ([int]$response.StatusCode -ge 500) {
            Add-Failure "$Path returned server error $($response.StatusCode)"
            return
        }

        Test-ExceptionContent $Path $content
        Test-SecurityHeaders $response $Path

        Write-Host "[PASS] $Path - $($response.StatusCode) - $Expectation - final=$finalUrl"
    } catch {
        Add-Failure "$Path request failed: $($_.Exception.Message)"
    }
}

function Test-HealthEndpoint {
    $path = "/healthz"
    $url = Join-Url $BaseUrl $path

    try {
        $response = Invoke-SmokeRequest $path
        $content = [string]$response.Content
        $finalUrl = Get-FinalUrl $response $url

        Test-ExceptionContent $path $content
        Test-SecurityHeaders $response $path

        try {
            $json = $content | ConvertFrom-Json
            if ($json.status -ne "ok") {
                Add-Failure "$path returned unexpected status '$($json.status)'"
                return
            }
        } catch {
            Add-Failure "$path did not return valid JSON"
            return
        }

        Write-Host "[PASS] $path - $($response.StatusCode) - liveness endpoint returns ok - final=$finalUrl"
    } catch {
        Add-Failure "$path request failed: $($_.Exception.Message)"
    }
}

function Test-ReadyEndpoint {
    $path = "/readyz"
    $url = Join-Url $BaseUrl $path

    try {
        $response = Invoke-SmokeRequest $path
        $content = [string]$response.Content
        $finalUrl = Get-FinalUrl $response $url

        Test-ExceptionContent $path $content
        Test-SecurityHeaders $response $path

        try {
            $json = $content | ConvertFrom-Json
            if ($json.status -ne "ready") {
                Add-Failure "$path returned unexpected status '$($json.status)'"
                return
            }

            if (-not [string]::IsNullOrWhiteSpace($ExpectedReadyProvider) -and
                -not [string]::Equals([string]$json.databaseProvider, $ExpectedReadyProvider, [StringComparison]::OrdinalIgnoreCase)) {
                Add-Failure "$path provider mismatch: expected '$ExpectedReadyProvider' got '$($json.databaseProvider)'"
                return
            }
        } catch {
            Add-Failure "$path did not return valid JSON"
            return
        }

        Write-Host "[PASS] $path - $($response.StatusCode) - database readiness endpoint returns ready - final=$finalUrl"
    } catch {
        Add-Failure "$path request failed: $($_.Exception.Message)"
    }
}

Write-Host "HTTP smoke check"
Write-Host "BaseUrl: $BaseUrl"
Write-Host "Security headers: $(if ($SkipSecurityHeaders) { 'skipped' } else { 'required' })"
Write-Host ""

Test-Page "/" "home page loads"
Test-HealthEndpoint
Test-Page "/Account/Login" "login page loads"
Test-Page "/Account/Register" "register page loads"
Test-Page "/AiRecommendation" "protected page redirects or loads"
Test-Page "/LolAramGuides" "protected page redirects or loads"
Test-Page "/LolAramAugments" "protected page redirects or loads"
Test-Page "/Equipment" "protected page redirects or loads"
Test-Page "/Calculator" "protected page redirects or loads"
Test-Page "/User/Profile" "protected page redirects or loads"

if ($CheckReadiness) {
    Test-ReadyEndpoint
}

Write-Host ""
Write-Host "Failures: $($failures.Count)"

if ($failures.Count -gt 0) {
    exit 1
}

Write-Host "HTTP_SMOKE_STATUS=PASS"
