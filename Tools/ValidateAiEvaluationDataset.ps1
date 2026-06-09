param(
    [string]$DatasetPath = "evaluation\aram-recommendation-cases.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$fullPath = if ([System.IO.Path]::IsPathRooted($DatasetPath)) {
    $DatasetPath
} else {
    Join-Path $repoRoot $DatasetPath
}
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)

    $failures.Add($Message) | Out-Null
    Write-Host "[FAIL] $Message"
}

function Require-Text {
    param(
        [object]$Case,
        [string]$PropertyName
    )

    $value = $Case.$PropertyName
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        Add-Failure "$($Case.id): missing $PropertyName"
    }
}

Write-Host "AI evaluation dataset validation"
Write-Host "Dataset: $fullPath"
Write-Host ""

if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    Add-Failure "dataset file is missing"
} else {
    try {
        $parsedCases = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $cases = if ($parsedCases -is [System.Array]) {
            $parsedCases
        } else {
            @($parsedCases)
        }
    } catch {
        Add-Failure "dataset JSON is invalid: $($_.Exception.Message)"
        $cases = @()
    }

    if ($cases.Count -ne 30) {
        Add-Failure "expected 30 cases, found $($cases.Count)"
    }

    $duplicateIds = @(
        $cases |
            Group-Object id |
            Where-Object { [string]::IsNullOrWhiteSpace($_.Name) -or $_.Count -ne 1 }
    )
    if ($duplicateIds.Count -gt 0) {
        Add-Failure "case IDs must be present and unique: $($duplicateIds.Name -join ', ')"
    }

    $openingStage = -join @([char]0x958B, [char]0x5C40)
    $levelSuffix = [string][char]0x7B49
    $guardianAngel = -join @([char]0x5B88, [char]0x8B77, [char]0x5929, [char]0x4F7F)
    $simplifiedGuardian = -join @([char]0x5B88, [char]0x62A4)
    $simplifiedLiandry = -join @([char]0x5170, [char]0x5FB7, [char]0x91CC)
    $burnItUp = -join @([char]0x71D2, [char]0x8D77, [char]0x4F86)
    $previousCountByStage = @{
        $openingStage = 0
        "7 $levelSuffix" = 1
        "11 $levelSuffix" = 2
        "15 $levelSuffix" = 3
    }
    $forbiddenItemFragments = @(
        "Guardian Angel",
        "Blackfire Torch",
        "Liandry",
        "Zhonya",
        "Rabadon",
        $simplifiedGuardian,
        $simplifiedLiandry
    )

    foreach ($case in $cases) {
        Require-Text $case "id"
        Require-Text $case "champion"
        Require-Text $case "roleCategory"
        Require-Text $case "stage"
        Require-Text $case "notes"

        if (-not $previousCountByStage.ContainsKey([string]$case.stage)) {
            Add-Failure "$($case.id): unsupported stage '$($case.stage)'"
        } else {
            $previousAugments = @($case.previousAugments)
            $expectedCount = $previousCountByStage[[string]$case.stage]
            if ($previousAugments.Count -ne $expectedCount) {
                Add-Failure "$($case.id): stage '$($case.stage)' requires $expectedCount previous augments"
            }
        }

        $offeredAugments = @($case.offeredAugments)
        if ($offeredAugments.Count -ne 3) {
            Add-Failure "$($case.id): offeredAugments must contain exactly three choices"
        } elseif (@($offeredAugments | Select-Object -Unique).Count -ne 3) {
            Add-Failure "$($case.id): offeredAugments contains duplicate choices"
        }

        if (@($case.requiredConcepts).Count -lt 3) {
            Add-Failure "$($case.id): requiredConcepts must contain at least three values"
        }

        $acceptableItems = @($case.acceptableItems)
        $forbiddenItems = @($case.forbiddenItems)
        if ($acceptableItems.Count -lt 4) {
            Add-Failure "$($case.id): acceptableItems must contain at least four values"
        }
        if ($forbiddenItems -notcontains $guardianAngel) {
            Add-Failure "$($case.id): forbiddenItems must include the ARAM-disabled guardian item"
        }
        if (@($case.forbiddenTerms).Count -eq 0) {
            Add-Failure "$($case.id): forbiddenTerms must not be empty"
        }

        $overlap = @($acceptableItems | Where-Object { $forbiddenItems -contains $_ })
        if ($overlap.Count -gt 0) {
            Add-Failure "$($case.id): acceptableItems overlaps forbiddenItems: $($overlap -join ', ')"
        }

        foreach ($item in $acceptableItems) {
            foreach ($fragment in $forbiddenItemFragments) {
                if ([string]$item -like "*$fragment*") {
                    Add-Failure "$($case.id): acceptable item '$item' contains non-canonical text '$fragment'"
                }
            }
        }

        if ([bool]$case.allowExhaust) {
            $allAugments = @($case.previousAugments) + @($case.offeredAugments)
            $hasBurnItUp = @($allAugments | Where-Object { [string]$_ -like "*$burnItUp*" }).Count -gt 0
            if (-not $hasBurnItUp) {
                Add-Failure "$($case.id): allowExhaust requires the conditional burn augment"
            }
        }
    }
}

Write-Host ""
Write-Host "CASES=$($cases.Count)"
Write-Host "FAILURES=$($failures.Count)"

if ($failures.Count -gt 0) {
    Write-Host "AI_EVALUATION_DATASET_STATUS=FAIL"
    exit 1
}

Write-Host "AI_EVALUATION_DATASET_STATUS=PASS"
