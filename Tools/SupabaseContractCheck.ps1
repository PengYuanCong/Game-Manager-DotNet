param(
    [string]$SchemaPath = "database\supabase\0001_schema.sql",
    [string]$ServicesPath = "Proposal\Services"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$schemaFullPath = Join-Path $repoRoot $SchemaPath
$servicesFullPath = Join-Path $repoRoot $ServicesPath
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

function New-StringSet {
    return New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)
}

function Get-RelativePath {
    param([string]$Path)

    if ($Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring($repoRoot.Length).TrimStart("\")
    }

    return $Path
}

function Test-Column {
    param(
        [hashtable]$Schema,
        [string]$Table,
        [string]$Column,
        [string]$Context
    )

    if (-not $Schema.ContainsKey($Table)) {
        Add-Failure "$Context references missing table public.$Table"
        return
    }

    if (-not $Schema[$Table].Contains($Column)) {
        Add-Failure "$Context references missing column public.$Table.$Column"
    }
}

function Test-ColumnExistsSomewhere {
    param(
        [hashtable]$Schema,
        [string]$Column,
        [string]$Context
    )

    foreach ($table in $Schema.Keys) {
        if ($Schema[$table].Contains($Column)) {
            return
        }
    }

    Add-Failure "$Context references unknown schema column $Column"
}

function Get-SchemaTables {
    param([string]$Sql)

    $schema = @{}
    $tablePattern = [regex]"(?is)create\s+table\s+if\s+not\s+exists\s+public\.(?<table>[a-z0-9_]+)\s*\((?<body>.*?)\);\s*"

    foreach ($match in $tablePattern.Matches($Sql)) {
        $table = $match.Groups["table"].Value
        $columns = New-StringSet

        foreach ($rawLine in ($match.Groups["body"].Value -split "\r?\n")) {
            $line = $rawLine.Trim().TrimEnd(",")
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            if ($line -match "^(constraint|primary|unique|foreign|check|exclude)\b") {
                continue
            }

            if ($line -match "^(?<column>[a-z][a-z0-9_]*)\s+") {
                $columns.Add($Matches["column"]) | Out-Null
            }
        }

        $schema[$table] = $columns
    }

    return $schema
}

function Get-SqlSegments {
    param([string]$Text)

    $segments = New-Object System.Collections.Generic.List[string]

    $rawStringPattern = [regex]'(?s)(?:\$)?"""\r?\n?(?<body>.*?)\r?\n?\s*"""'
    foreach ($match in $rawStringPattern.Matches($Text)) {
        $body = $match.Groups["body"].Value
        if ($body -match "public\.") {
            $segments.Add($body) | Out-Null
        }
    }

    $normalStringPattern = [regex]'(?s)"(?<body>(?:\\.|[^"\\])*)"'
    foreach ($match in $normalStringPattern.Matches($Text)) {
        $body = $match.Groups["body"].Value
        if ($body -match "public\.") {
            $segments.Add($body) | Out-Null
        }
    }

    return $segments
}

function Test-ColumnConstants {
    param(
        [hashtable]$Schema,
        [string]$Text,
        [string]$RelativePath
    )

    $constantTables = @{
        EquipmentColumns = "equipments"
        GuideColumns = "lol_aram_guides"
        AugmentColumns = "lol_aram_augments"
    }

    $constantPattern = [regex]'(?s)const\s+string\s+(?<name>[A-Za-z0-9_]*Columns)\s*=\s*"""\r?\n?(?<body>.*?)\r?\n?\s*""";'
    foreach ($match in $constantPattern.Matches($Text)) {
        $name = $match.Groups["name"].Value
        if (-not $constantTables.ContainsKey($name)) {
            continue
        }

        $table = $constantTables[$name]
        foreach ($columnMatch in [regex]::Matches($match.Groups["body"].Value, "\b[a-z][a-z0-9_]*\b")) {
            Test-Column $Schema $table $columnMatch.Value "${RelativePath}:$name"
        }
    }
}

function Test-SqlSegment {
    param(
        [hashtable]$Schema,
        [string]$Sql,
        [string]$RelativePath
    )

    $sqlKeywords = New-StringSet
    @(
        "and", "as", "asc", "case", "desc", "do", "else", "end", "excluded",
        "from", "inner", "insert", "into", "is", "join", "left", "limit",
        "not", "null", "on", "or", "order", "over", "partition", "ranked",
        "select", "set", "then", "update", "values", "when", "where"
    ) | ForEach-Object { $sqlKeywords.Add($_) | Out-Null }

    foreach ($tableMatch in [regex]::Matches($Sql, "\bpublic\.(?<table>[a-z0-9_]+)\b")) {
        $table = $tableMatch.Groups["table"].Value
        if (-not $Schema.ContainsKey($table)) {
            Add-Failure "$RelativePath references missing table public.$table"
        }
    }

    foreach ($columnMatch in [regex]::Matches($Sql, "\bpublic\.(?<table>[a-z0-9_]+)\.(?<column>[a-z0-9_]+)\b")) {
        Test-Column $Schema $columnMatch.Groups["table"].Value $columnMatch.Groups["column"].Value $RelativePath
    }

    foreach ($insertMatch in [regex]::Matches($Sql, "(?is)insert\s+into\s+public\.(?<table>[a-z0-9_]+)\s*\((?<columns>.*?)\)\s*values")) {
        $table = $insertMatch.Groups["table"].Value
        foreach ($columnMatch in [regex]::Matches($insertMatch.Groups["columns"].Value, "\b[a-z][a-z0-9_]*\b")) {
            Test-Column $Schema $table $columnMatch.Value "$RelativePath insert"
        }
    }

    foreach ($updateMatch in [regex]::Matches($Sql, "(?is)update\s+public\.(?<table>[a-z0-9_]+)\s+set\s+(?<assignments>.*?)(?:\bwhere\b|;)")) {
        $table = $updateMatch.Groups["table"].Value
        foreach ($columnMatch in [regex]::Matches($updateMatch.Groups["assignments"].Value, "(?:^|,)\s*(?<column>[a-z][a-z0-9_]*)\s*=")) {
            Test-Column $Schema $table $columnMatch.Groups["column"].Value "$RelativePath update"
        }
    }

    foreach ($upsertMatch in [regex]::Matches($Sql, "(?is)insert\s+into\s+public\.(?<table>[a-z0-9_]+).*?do\s+update\s+set\s+(?<assignments>.*?);")) {
        $table = $upsertMatch.Groups["table"].Value
        foreach ($columnMatch in [regex]::Matches($upsertMatch.Groups["assignments"].Value, "(?:^|,)\s*(?<column>[a-z][a-z0-9_]*)\s*=")) {
            Test-Column $Schema $table $columnMatch.Groups["column"].Value "$RelativePath upsert"
        }
    }

    $aliases = @{}
    $aliasPattern = [regex]"(?is)\b(?:from|join|update|delete\s+from)\s+public\.(?<table>[a-z0-9_]+)(?:\s+(?:as\s+)?(?<alias>[a-z][a-z0-9_]*))?"
    foreach ($aliasMatch in $aliasPattern.Matches($Sql)) {
        $table = $aliasMatch.Groups["table"].Value
        $alias = $aliasMatch.Groups["alias"].Value
        if (-not [string]::IsNullOrWhiteSpace($alias) -and -not $sqlKeywords.Contains($alias)) {
            $aliases[$alias] = $table
        }
    }

    foreach ($aliasColumnMatch in [regex]::Matches($Sql, "\b(?<alias>[a-z][a-z0-9_]*)\.(?<column>[a-z][a-z0-9_]*)\b")) {
        $alias = $aliasColumnMatch.Groups["alias"].Value
        if ($aliases.ContainsKey($alias)) {
            Test-Column $Schema $aliases[$alias] $aliasColumnMatch.Groups["column"].Value "$RelativePath alias '$alias'"
        }
    }
}

function Test-ReaderColumns {
    param(
        [hashtable]$Schema,
        [string]$Text,
        [string]$RelativePath
    )

    $patterns = @(
        'reader\[\s*"(?<column>[a-z][a-z0-9_]*)"\s*\]',
        'Read(?:Nullable)?(?:String|Int32|Decimal|DateTime)\(\s*reader\s*,\s*"(?<column>[a-z][a-z0-9_]*)"'
    )

    foreach ($pattern in $patterns) {
        foreach ($match in [regex]::Matches($Text, $pattern)) {
            $column = $match.Groups["column"].Value
            if ($column.StartsWith("total_", [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            Test-ColumnExistsSomewhere $Schema $column "$RelativePath reader"
        }
    }
}

if (-not (Test-Path -LiteralPath $schemaFullPath)) {
    Add-Failure "missing schema file: $SchemaPath"
}

if (-not (Test-Path -LiteralPath $servicesFullPath -PathType Container)) {
    Add-Failure "missing services directory: $ServicesPath"
}

if ($failures.Count -eq 0) {
    Write-Host "Supabase contract check"
    Write-Host "Schema: $SchemaPath"
    Write-Host "Services: $ServicesPath"
    Write-Host ""

    $schemaSql = [System.IO.File]::ReadAllText($schemaFullPath)
    $schema = Get-SchemaTables $schemaSql
    $expectedTables = @(
        "app_users",
        "user_activity_logs",
        "calculation_history",
        "equipments",
        "equipment_loadouts",
        "ai_recommendation_cache",
        "ai_recommendation_favorites",
        "lol_aram_guides",
        "lol_aram_augment_series",
        "lol_aram_augments",
        "lol_aram_items",
        "lol_aram_synergy_rules"
    )

    foreach ($table in $expectedTables) {
        if ($schema.ContainsKey($table)) {
            Write-Check "PASS" "schema table exists" "public.$table"
        } else {
            Add-Failure "schema missing required table public.$table"
        }

        if ($schemaSql -match "alter\s+table\s+public\.$([regex]::Escape($table))\s+enable\s+row\s+level\s+security\s*;") {
            Write-Check "PASS" "RLS enabled" "public.$table"
        } else {
            Add-Failure "schema missing RLS enable statement for public.$table"
        }
    }

    $postgresFiles = Get-ChildItem -LiteralPath $servicesFullPath -Filter "Postgres*.cs" -File |
        Sort-Object Name

    foreach ($file in $postgresFiles) {
        $relativePath = Get-RelativePath $file.FullName
        $text = [System.IO.File]::ReadAllText($file.FullName)
        Test-ColumnConstants $schema $text $relativePath
        Test-ReaderColumns $schema $text $relativePath

        foreach ($segment in Get-SqlSegments $text) {
            Test-SqlSegment $schema $segment $relativePath
        }
    }

    if ($failures.Count -eq 0) {
        Write-Check "PASS" "Postgres service references matched Supabase schema" "$($postgresFiles.Count) files"
    }
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

Write-Host "SUPABASE_CONTRACT_STATUS=PASS"
