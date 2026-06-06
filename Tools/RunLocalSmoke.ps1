param(
    [int]$Port = 5226,

    [int]$StartupTimeoutSeconds = 45,

    [ValidateSet("HttpSmoke", "CloudPreview")]
    [string]$Gate = "HttpSmoke",

    [switch]$CheckReadiness,

    [string]$ExpectedReadyProvider = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "Proposal\Proposal.csproj"
$dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$baseUrl = "http://localhost:$Port"

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetPath = "dotnet"
}

function Set-ChildPath {
    param([System.Collections.Specialized.StringDictionary]$EnvironmentVariables)

    $pathValue = [Environment]::GetEnvironmentVariable("Path", "Process")
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        $pathValue = [Environment]::GetEnvironmentVariable("PATH", "Process")
    }

    if (-not [string]::IsNullOrWhiteSpace($pathValue)) {
        $EnvironmentVariables["Path"] = $pathValue
    }
}

function Wait-ForHealth {
    param(
        [string]$Url,
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
    $lastError = ""

    while ([DateTimeOffset]::Now -lt $deadline) {
        if ($Process.HasExited) {
            throw "local smoke server exited before health check. ExitCode=$($Process.ExitCode)"
        }

        try {
            $response = Invoke-WebRequest -Uri "$Url/healthz" -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -eq 200) {
                return
            }
        } catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Milliseconds 750
    }

    throw "local smoke server did not become healthy in $TimeoutSeconds seconds. LastError=$lastError"
}

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $dotnetPath
$psi.Arguments = "run --no-build --no-launch-profile --project `"$projectPath`" --urls `"$baseUrl`""
$psi.WorkingDirectory = $repoRoot
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
if ($psi.EnvironmentVariables) {
    $psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
    Set-ChildPath $psi.EnvironmentVariables
} else {
    Write-Host "WARN: ProcessStartInfo.EnvironmentVariables is not available in this host; inheriting process environment."
}

$process = $null
$smokeExitCode = 1

try {
    Write-Host "Starting local smoke server"
    Write-Host "BaseUrl: $baseUrl"
    $process = [System.Diagnostics.Process]::Start($psi)
    Write-Host "PID: $($process.Id)"

    Wait-ForHealth $baseUrl $process $StartupTimeoutSeconds

    if ($Gate -eq "CloudPreview") {
        $args = @(
            "-ExecutionPolicy", "Bypass",
            "-File", "Tools\RunCloudPreviewSmoke.ps1",
            "-Target", "Generic",
            "-BaseUrl", $baseUrl,
            "-AllowHttp"
        )

        if (-not $CheckReadiness) {
            $args += "-SkipReadiness"
        }

        if ($CheckReadiness -and -not [string]::IsNullOrWhiteSpace($ExpectedReadyProvider)) {
            $args += "-ExpectedReadyProvider"
            $args += $ExpectedReadyProvider
        }
    } else {
        $args = @(
            "-ExecutionPolicy", "Bypass",
            "-File", "Tools\HttpSmokeCheck.ps1",
            "-BaseUrl", $baseUrl
        )

        if ($CheckReadiness) {
            $args += "-CheckReadiness"
        }

        if (-not [string]::IsNullOrWhiteSpace($ExpectedReadyProvider)) {
            $args += "-ExpectedReadyProvider"
            $args += $ExpectedReadyProvider
        }
    }

    & powershell @args
    $smokeExitCode = $LASTEXITCODE
} finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }

    if ($process) {
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()

        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Host ""
            Write-Host "Server stdout tail:"
            $stdout -split "`r?`n" | Select-Object -Last 30 | ForEach-Object { Write-Host $_ }
        }

        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Host ""
            Write-Host "Server stderr tail:"
            $stderr -split "`r?`n" | Select-Object -Last 30 | ForEach-Object { Write-Host $_ }
        }
    }
}

exit $smokeExitCode
