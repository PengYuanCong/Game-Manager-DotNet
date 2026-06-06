param(
    [string]$Project = "Proposal\Proposal.csproj",
    [string]$Output = "build-check\proposal-safe-build"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath "build-check")) {
    New-Item -ItemType Directory -Path "build-check" | Out-Null
}

$logName = ($Output -replace '[\\/:*?"<>|]', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($logName)) {
    $logName = "safe-build"
}

$logPath = Join-Path "build-check" "$logName.log"
$args = @(
    "build",
    $Project,
    "--no-restore",
    "-p:UseAppHost=false",
    "-o",
    $Output
)

& dotnet @args *> $logPath
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    $warningCount = (Select-String -Path $logPath -Pattern "warning " -SimpleMatch).Count
    "BUILD_STATUS=SUCCESS"
    "WARNINGS=$warningCount"
    "LOG=$logPath"
    exit 0
}

"BUILD_STATUS=FAILED"
"LOG=$logPath"
Get-Content -Path $logPath -Tail 80
exit $exitCode
