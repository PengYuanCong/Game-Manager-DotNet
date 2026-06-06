param(
    [int]$Port = 5214,
    [switch]$NoRun,
    [switch]$Detached
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "Proposal\Proposal.csproj"
$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

# Codex/PowerShell sessions can inherit both Path and PATH. Start-Process then
# crashes because .NET treats them as duplicate dictionary keys. Normalize the
# current process before running dotnet.
$processEnv = [Environment]::GetEnvironmentVariables("Process")
$pathNames = @($processEnv.Keys | Where-Object { $_ -ieq "Path" })
if ($pathNames.Count -gt 1) {
    $preferredName = $pathNames | Where-Object { $_ -ceq "Path" } | Select-Object -First 1
    if (-not $preferredName) {
        $preferredName = $pathNames | Select-Object -First 1
    }

    $pathValue = [Environment]::GetEnvironmentVariable($preferredName, "Process")
    foreach ($name in $pathNames) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }
    [Environment]::SetEnvironmentVariable("Path", $pathValue, "Process")
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$url = "http://localhost:$Port"

Write-Host "Dev server command:"
Write-Host "  $dotnet run --project `"$project`" --urls $url"
Write-Host ""

if ($NoRun) {
    $remainingPathNames = @([Environment]::GetEnvironmentVariables("Process").Keys | Where-Object { $_ -ieq "Path" })
    Write-Host "Path variables after normalization: $($remainingPathNames -join ', ')"
    exit 0
}

if ($Detached) {
    $cmdLine = "start `"Proposal Dev Server`" /min `"$dotnet`" run --project `"$project`" --urls `"$url`""
    & $env:ComSpec /c $cmdLine
    Write-Host "URL: $url"
    exit 0
}

& $dotnet run --project $project --urls $url
