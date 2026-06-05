using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

var options = MigrationOptions.Parse(args);
if (!options.IsValid)
{
    MigrationOptions.PrintUsage();
    return 2;
}

if (options.InitializeSchema)
{
    if (!options.ConfirmStaging)
    {
        Console.WriteLine("ERROR=Schema initialization requires --confirm-staging or MIGRATION_CONFIRM_STAGING=true.");
        Console.WriteLine("RESULT=No schema changes were written.");
        return 2;
    }

    return await InitializeSchemaAsync(options);
}

if (options.Apply && !options.ConfirmStaging)
{
    Console.WriteLine("ERROR=Apply mode requires --confirm-staging or MIGRATION_CONFIRM_STAGING=true.");
    Console.WriteLine("RESULT=No data was written.");
    return 2;
}

Console.WriteLine(options.Apply
    ? "MODE=APPLY"
    : "MODE=DRY_RUN");
Console.WriteLine("SOURCE=SQL Server");
Console.WriteLine("TARGET=PostgreSQL/Supabase");

await using var source = new SqlConnection(options.SourceConnectionString);
await using var target = new NpgsqlConnection(options.TargetConnectionString);

await source.OpenAsync();
await target.OpenAsync();

var selectedTables = TableSpecs.All
    .Where(spec => options.Tables.Count == 0 || options.Tables.Contains(spec.Name, StringComparer.OrdinalIgnoreCase))
    .ToList();

if (selectedTables.Count == 0)
{
    Console.WriteLine("ERROR=No matching tables selected.");
    return 2;
}

var totalRows = 0;
var preflights = new List<TablePreflight>();
foreach (var spec in selectedTables)
{
    var targetValidation = await ValidateTargetSchemaAsync(target, spec);
    var sourceExists = await SourceTableExistsAsync(source, spec.SourceTable);
    ColumnValidation? sourceValidation = null;
    var sourceRows = 0;
    int? targetRows = null;

    if (targetValidation.TableExists)
    {
        targetRows = await CountTargetRowsAsync(target, spec);
    }

    if (sourceExists)
    {
        sourceValidation = await ValidateColumnsAsync(source, spec);
        sourceRows = await CountSourceRowsAsync(source, spec);
        totalRows += sourceRows;
    }

    var preflight = new TablePreflight(
        spec,
        sourceExists,
        sourceValidation,
        targetValidation,
        sourceRows,
        targetRows);
    preflights.Add(preflight);
    PrintPreflight(preflight);
}

var hasErrors = preflights.Any(preflight => preflight.HasErrors);
Console.WriteLine($"TOTAL_SOURCE_ROWS={totalRows}");

if (!options.Apply)
{
    Console.WriteLine(hasErrors
        ? "RESULT=Dry run found errors. Fix them before running --apply."
        : "RESULT=Dry run completed. Re-run with --apply --confirm-staging only after schema and row counts look correct.");

    return hasErrors ? 1 : 0;
}

if (hasErrors)
{
    Console.WriteLine("RESULT=Apply aborted before writing because preflight found errors.");
    return 1;
}

await using var transaction = await target.BeginTransactionAsync();
try
{
    foreach (var preflight in preflights)
    {
        if (!preflight.SourceExists || preflight.SourceValidation is null)
        {
            Console.WriteLine($"TABLE={preflight.Spec.Name}; STATUS=SKIPPED; REASON=missing source table {preflight.Spec.SourceTable}");
            continue;
        }

        var copied = await CopyTableAsync(source, target, transaction, preflight.Spec, preflight.SourceValidation.ActiveColumns);
        if (preflight.Spec.HasIdentity)
        {
            await ResetSequenceAsync(target, transaction, preflight.Spec.TargetTable);
        }

        var targetRowsAfter = await CountTargetRowsInTransactionAsync(target, transaction, preflight.Spec);
        Console.WriteLine(
            $"TABLE={preflight.Spec.Name}; SOURCE_ROWS={preflight.SourceRows}; COPIED={copied}; TARGET_ROWS_BEFORE={preflight.TargetRows?.ToString() ?? "unknown"}; TARGET_ROWS_AFTER={targetRowsAfter}; STATUS=OK");
    }

    await transaction.CommitAsync();
    Console.WriteLine("RESULT=Migration copy completed. Run app smoke tests against staging before production use.");
    return 0;
}
catch (Exception ex) when (ex is DbException or InvalidOperationException)
{
    await transaction.RollbackAsync();
    Console.WriteLine("RESULT=Migration failed. PostgreSQL transaction was rolled back.");
    Console.WriteLine($"ERROR_TYPE={ex.GetType().Name}");
    Console.WriteLine($"ERROR_MESSAGE={SanitizeErrorMessage(ex.Message)}");
    return 1;
}

static async Task<int> InitializeSchemaAsync(MigrationOptions options)
{
    var schemaPath = Path.GetFullPath(
        Path.Combine(Directory.GetCurrentDirectory(), "database", "supabase", "0001_schema.sql"));

    if (!File.Exists(schemaPath))
    {
        Console.WriteLine("ERROR=Schema file database/supabase/0001_schema.sql was not found.");
        Console.WriteLine("RESULT=No schema changes were written.");
        return 2;
    }

    var schemaSql = await File.ReadAllTextAsync(schemaPath);
    if (string.IsNullOrWhiteSpace(schemaSql))
    {
        Console.WriteLine("ERROR=Schema file is empty.");
        Console.WriteLine("RESULT=No schema changes were written.");
        return 2;
    }

    Console.WriteLine("MODE=INITIALIZE_SCHEMA");
    Console.WriteLine("TARGET=PostgreSQL/Supabase staging");

    await using var target = new NpgsqlConnection(options.TargetConnectionString);
    try
    {
        await target.OpenAsync();
        await using var command = new NpgsqlCommand(schemaSql, target);
        await command.ExecuteNonQueryAsync();

        foreach (var spec in TableSpecs.All)
        {
            var validation = await ValidateTargetSchemaAsync(target, spec);
            if (!validation.TableExists
                || validation.MissingColumns.Count > 0
                || !validation.RlsEnabled)
            {
                Console.WriteLine($"ERROR=Schema verification failed for public.{spec.TargetTable}.");
                Console.WriteLine("RESULT=Schema initialization did not pass verification.");
                return 1;
            }
        }

        Console.WriteLine($"TABLES_VERIFIED={TableSpecs.All.Count}");
        Console.WriteLine("RESULT=Supabase staging schema initialized and verified.");
        return 0;
    }
    catch (Exception ex) when (ex is DbException or InvalidOperationException)
    {
        Console.WriteLine("RESULT=Schema initialization failed. PostgreSQL rolled back the schema transaction.");
        Console.WriteLine($"ERROR_TYPE={ex.GetType().Name}");
        Console.WriteLine($"ERROR_MESSAGE={SanitizeErrorMessage(ex.Message)}");
        return 1;
    }
}

static async Task<bool> SourceTableExistsAsync(SqlConnection connection, string tableName)
{
    const string sql = """
        select count(1)
        from sys.tables t
        inner join sys.schemas s on s.schema_id = t.schema_id
        where s.name = N'dbo' and t.name = @table_name;
        """;

    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@table_name", SqlDbType.NVarChar, 128).Value = tableName;
    return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
}

static async Task<int> CountSourceRowsAsync(SqlConnection connection, TableSpec spec)
{
    await using var command = new SqlCommand($"select count(1) from dbo.[{spec.SourceTable}];", connection);
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static async Task<int> CountTargetRowsAsync(NpgsqlConnection connection, TableSpec spec)
{
    await using var command = new NpgsqlCommand($"select count(1) from public.{spec.TargetTable};", connection);
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static async Task<int> CountTargetRowsInTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TableSpec spec)
{
    await using var command = new NpgsqlCommand($"select count(1) from public.{spec.TargetTable};", connection);
    command.Transaction = transaction;
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static async Task<TargetValidation> ValidateTargetSchemaAsync(NpgsqlConnection connection, TableSpec spec)
{
    const string tableSql = """
        select exists (
            select 1
            from information_schema.tables
            where table_schema = 'public' and table_name = @table_name
        );
        """;

    await using (var tableCommand = new NpgsqlCommand(tableSql, connection))
    {
        tableCommand.Parameters.Add("@table_name", NpgsqlDbType.Varchar).Value = spec.TargetTable;
        var tableExists = Convert.ToBoolean(await tableCommand.ExecuteScalarAsync());
        if (!tableExists)
        {
            return new TargetValidation(
                TableExists: false,
                MissingColumns: spec.Columns.Select(column => column.Target).ToList(),
                RlsEnabled: false);
        }
    }

    const string columnSql = """
        select column_name
        from information_schema.columns
        where table_schema = 'public' and table_name = @table_name;
        """;

    var targetColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var columnCommand = new NpgsqlCommand(columnSql, connection))
    {
        columnCommand.Parameters.Add("@table_name", NpgsqlDbType.Varchar).Value = spec.TargetTable;
        await using var reader = await columnCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            targetColumns.Add(reader.GetString(0));
        }
    }

    const string rlsSql = """
        select c.relrowsecurity
        from pg_class c
        inner join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = 'public' and c.relname = @table_name;
        """;

    var rlsEnabled = false;
    await using (var rlsCommand = new NpgsqlCommand(rlsSql, connection))
    {
        rlsCommand.Parameters.Add("@table_name", NpgsqlDbType.Varchar).Value = spec.TargetTable;
        rlsEnabled = Convert.ToBoolean(await rlsCommand.ExecuteScalarAsync() ?? false);
    }

    var missingColumns = spec.Columns
        .Select(column => column.Target)
        .Where(column => !targetColumns.Contains(column))
        .ToList();

    return new TargetValidation(
        TableExists: true,
        MissingColumns: missingColumns,
        RlsEnabled: rlsEnabled);
}

static async Task<ColumnValidation> ValidateColumnsAsync(SqlConnection connection, TableSpec spec)
{
    const string sql = """
        select c.name
        from sys.columns c
        inner join sys.tables t on t.object_id = c.object_id
        inner join sys.schemas s on s.schema_id = t.schema_id
        where s.name = N'dbo' and t.name = @table_name;
        """;

    var sourceColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = new SqlCommand(sql, connection))
    {
        command.Parameters.Add("@table_name", SqlDbType.NVarChar, 128).Value = spec.SourceTable;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sourceColumns.Add(reader.GetString(0));
        }
    }

    var active = new List<ColumnMap>();
    var missingRequired = new List<string>();
    var missingOptional = new List<string>();

    foreach (var column in spec.Columns)
    {
        if (sourceColumns.Contains(column.Source))
        {
            active.Add(column);
        }
        else if (column.Required)
        {
            missingRequired.Add(column.Source);
        }
        else
        {
            missingOptional.Add(column.Source);
        }
    }

    return new ColumnValidation(active, missingRequired, missingOptional);
}

static async Task<int> CopyTableAsync(
    SqlConnection source,
    NpgsqlConnection target,
    NpgsqlTransaction transaction,
    TableSpec spec,
    IReadOnlyList<ColumnMap> columns)
{
    var copied = 0;
    var sourceColumns = string.Join(", ", columns.Select(column => $"[{column.Source}]"));
    await using var selectCommand = new SqlCommand(
        $"select {sourceColumns} from dbo.[{spec.SourceTable}] order by {spec.OrderBy};",
        source);

    await using var reader = await selectCommand.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        await using var insertCommand = new NpgsqlCommand(BuildUpsertSql(spec, columns), target);
        insertCommand.Transaction = transaction;
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var value = reader[column.Source];
            var parameter = insertCommand.Parameters.Add($"@p{index}", column.Type);
            parameter.Value = NormalizeValue(value, column);
        }

        await insertCommand.ExecuteNonQueryAsync();
        copied++;
    }

    return copied;
}

static object NormalizeValue(object value, ColumnMap column)
{
    if (value == DBNull.Value)
    {
        return DBNull.Value;
    }

    if (column.Type == NpgsqlDbType.TimestampTz && value is DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();
    }

    return value;
}

static string BuildUpsertSql(TableSpec spec, IReadOnlyList<ColumnMap> columns)
{
    var targetColumns = string.Join(", ", columns.Select(column => column.Target));
    var parameters = string.Join(", ", columns.Select((_, index) => $"@p{index}"));
    var updates = string.Join(
        ", ",
        columns
            .Where(column => !spec.UpdateExclusions.Contains(column.Target, StringComparer.OrdinalIgnoreCase))
            .Select(column => $"{column.Target} = excluded.{column.Target}"));

    if (string.IsNullOrWhiteSpace(updates))
    {
        return $"""
            insert into public.{spec.TargetTable} ({targetColumns})
            values ({parameters})
            on conflict {spec.ConflictTarget} do nothing;
            """;
    }

    return $"""
        insert into public.{spec.TargetTable} ({targetColumns})
        values ({parameters})
        on conflict {spec.ConflictTarget} do update
        set {updates};
        """;
}

static async Task ResetSequenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string targetTable)
{
    var sql = $"""
        select setval(
            pg_get_serial_sequence('public.{targetTable}', 'id'),
            coalesce((select max(id) from public.{targetTable}), 1),
            (select count(*) > 0 from public.{targetTable})
        );
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Transaction = transaction;
    await command.ExecuteNonQueryAsync();
}

static void PrintPreflight(TablePreflight preflight)
{
    var spec = preflight.Spec;

    if (!preflight.TargetValidation.TableExists)
    {
        Console.WriteLine($"TABLE={spec.Name}; STATUS=ERROR; REASON=missing target table public.{spec.TargetTable}");
    }
    else if (preflight.TargetValidation.MissingColumns.Count > 0)
    {
        Console.WriteLine(
            $"TABLE={spec.Name}; STATUS=ERROR; REASON=missing target columns {string.Join(",", preflight.TargetValidation.MissingColumns)}");
    }
    else if (!preflight.TargetValidation.RlsEnabled)
    {
        Console.WriteLine($"TABLE={spec.Name}; STATUS=ERROR; REASON=target table public.{spec.TargetTable} does not have RLS enabled");
    }

    if (!preflight.SourceExists)
    {
        Console.WriteLine($"TABLE={spec.Name}; STATUS=SKIPPED; REASON=missing source table {spec.SourceTable}");
        return;
    }

    if (preflight.SourceValidation is null)
    {
        Console.WriteLine($"TABLE={spec.Name}; STATUS=ERROR; REASON=source validation did not run");
        return;
    }

    if (preflight.SourceValidation.MissingRequired.Count > 0)
    {
        Console.WriteLine(
            $"TABLE={spec.Name}; STATUS=ERROR; REASON=missing required source columns {string.Join(",", preflight.SourceValidation.MissingRequired)}");
    }

    if (preflight.SourceValidation.MissingOptional.Count > 0)
    {
        Console.WriteLine(
            $"TABLE={spec.Name}; OPTIONAL_COLUMNS_OMITTED={string.Join(",", preflight.SourceValidation.MissingOptional)}");
    }

    if (!preflight.HasErrors)
    {
        Console.WriteLine(
            $"TABLE={spec.Name}; SOURCE_ROWS={preflight.SourceRows}; TARGET_ROWS={preflight.TargetRows?.ToString() ?? "unknown"}; STATUS=PREFLIGHT_OK");
    }
}

static string SanitizeErrorMessage(string message)
{
    return message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown error";
}

sealed record ColumnMap(string Source, string Target, NpgsqlDbType Type, bool Required = true);

sealed record ColumnValidation(
    IReadOnlyList<ColumnMap> ActiveColumns,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<string> MissingOptional);

sealed record TargetValidation(
    bool TableExists,
    IReadOnlyList<string> MissingColumns,
    bool RlsEnabled);

sealed record TablePreflight(
    TableSpec Spec,
    bool SourceExists,
    ColumnValidation? SourceValidation,
    TargetValidation TargetValidation,
    int SourceRows,
    int? TargetRows)
{
    public bool HasErrors =>
        !TargetValidation.TableExists
        || TargetValidation.MissingColumns.Count > 0
        || !TargetValidation.RlsEnabled
        || SourceValidation?.MissingRequired.Count > 0;
}

sealed record TableSpec(
    string Name,
    string SourceTable,
    string TargetTable,
    string ConflictTarget,
    string OrderBy,
    bool HasIdentity,
    IReadOnlyList<ColumnMap> Columns,
    IReadOnlySet<string> UpdateExclusions);

static class TableSpecs
{
    private static readonly NpgsqlDbType Text = NpgsqlDbType.Varchar;
    private static readonly NpgsqlDbType Int = NpgsqlDbType.Integer;
    private static readonly NpgsqlDbType BigInt = NpgsqlDbType.Bigint;
    private static readonly NpgsqlDbType Numeric = NpgsqlDbType.Numeric;
    private static readonly NpgsqlDbType Time = NpgsqlDbType.TimestampTz;
    private static readonly NpgsqlDbType Jsonb = NpgsqlDbType.Jsonb;

    public static IReadOnlyList<TableSpec> All { get; } = new[]
    {
        new TableSpec(
            "users",
            "Users",
            "app_users",
            "((lower(username)))",
            "Username",
            HasIdentity: false,
            Columns: new[]
            {
                C("Username", "username", Text),
                C("Password", "password_hash", Text)
            },
            UpdateExclusions: new HashSet<string>()),

        WithId(
            "calculation_history",
            "CalculationHistory",
            "calculation_history",
            new[]
            {
                C("Id", "id", BigInt),
                C("Username", "username", Text),
                C("FormulaType", "formula_type", Text),
                C("InputDetails", "input_details", Text),
                C("ResultContent", "result_content", Text),
                O("CreatedAt", "created_at", Time)
            }),

        WithId(
            "user_activity_logs",
            "UserActivityLogs",
            "user_activity_logs",
            new[]
            {
                C("Id", "id", BigInt),
                C("Username", "username", Text),
                C("Category", "category", Text),
                C("Title", "title", Text),
                C("Detail", "detail", Text),
                O("LinkUrl", "link_url", Text),
                O("CreatedAt", "created_at", Time)
            }),

        WithId(
            "equipments",
            "Equipments",
            "equipments",
            new[]
            {
                C("Id", "id", BigInt),
                C("Username", "owner_username", Text),
                C("Name", "name", Text),
                C("HP", "hp", Int),
                O("Mana", "mana", Int),
                C("Attack", "attack", Int),
                C("MagicAttack", "magic_attack", Int),
                C("PhysicalDefense", "physical_defense", Int),
                C("MagicDefense", "magic_defense", Int),
                O("HealthRegen", "health_regen", Numeric),
                O("ManaRegen", "mana_regen", Numeric),
                O("AbilityHaste", "ability_haste", Numeric),
                O("AttackSpeed", "attack_speed", Numeric),
                O("CriticalStrikeChance", "critical_strike_chance", Numeric),
                O("MoveSpeed", "move_speed", Int),
                O("MoveSpeedPercent", "move_speed_percent", Numeric),
                O("Lethality", "lethality", Numeric),
                O("ArmorPenetrationPercent", "armor_penetration_percent", Numeric),
                O("MagicPenetration", "magic_penetration", Numeric),
                O("MagicPenetrationPercent", "magic_penetration_percent", Numeric),
                O("LifeSteal", "life_steal", Numeric),
                O("Omnivamp", "omnivamp", Numeric),
                O("HealAndShieldPower", "heal_and_shield_power", Numeric),
                O("Tenacity", "tenacity", Numeric),
                C("Price", "price", Int),
                O("DataDragonId", "data_dragon_id", Text),
                O("ItemImageUrl", "item_image_url", Text),
                O("ItemTags", "item_tags", Text),
                O("ItemDescription", "item_description", Text)
            }),

        WithId(
            "equipment_loadouts",
            "Loadouts",
            "equipment_loadouts",
            new[]
            {
                C("Id", "id", BigInt),
                C("Username", "owner_username", Text),
                C("LoadoutName", "loadout_name", Text),
                C("Eq1_Id", "eq1_id", BigInt),
                C("Eq2_Id", "eq2_id", BigInt),
                C("Eq3_Id", "eq3_id", BigInt),
                C("Eq4_Id", "eq4_id", BigInt),
                C("Eq5_Id", "eq5_id", BigInt),
                C("Eq6_Id", "eq6_id", BigInt),
                O("CreatedAt", "created_at", Time)
            }),

        WithId(
            "ai_recommendation_cache",
            "AiRecommendationCache",
            "ai_recommendation_cache",
            new[]
            {
                C("Id", "id", BigInt),
                C("Username", "username", Text),
                C("CacheKey", "cache_key", Text),
                C("GameTitle", "game_title", Text),
                C("CoreChampion", "core_champion", Text),
                O("CurrentStage", "current_stage", Text),
                O("Augment", "augment", Text),
                O("AvailableItems", "available_items", Text),
                O("Notes", "notes", Text),
                C("RecommendationJson", "recommendation_json", Jsonb),
                O("CreatedAt", "created_at", Time),
                O("LastUsedAt", "last_used_at", Time),
                O("HitCount", "hit_count", Int)
            }),

        WithId(
            "ai_recommendation_favorites",
            "AiRecommendationFavorites",
            "ai_recommendation_favorites",
            new[]
            {
                C("Id", "id", BigInt),
                C("Username", "username", Text),
                C("InputHash", "input_hash", Text),
                C("Title", "title", Text),
                C("GameTitle", "game_title", Text),
                C("CoreChampion", "core_champion", Text),
                O("CurrentStage", "current_stage", Text),
                O("Augment", "augment", Text),
                O("AvailableItems", "available_items", Text),
                O("Notes", "notes", Text),
                C("Summary", "summary", Text),
                C("RecommendedItems", "recommended_items", Text),
                C("RecommendedAugments", "recommended_augments", Text),
                C("InputJson", "input_json", Jsonb),
                C("RecommendationJson", "recommendation_json", Jsonb),
                O("AdoptedCount", "adopted_count", Int),
                O("LastAdoptedAt", "last_adopted_at", Time),
                O("CreatedAt", "created_at", Time),
                O("UpdatedAt", "updated_at", Time)
            }),

        WithId(
            "lol_aram_guides",
            "LolAramGuides",
            "lol_aram_guides",
            new[]
            {
                C("Id", "id", BigInt),
                C("ChampionKey", "champion_key", Text),
                C("ChampionName", "champion_name", Text),
                O("LocalizedName", "localized_name", Text),
                C("ModeName", "mode_name", Text),
                C("PatchVersion", "patch_version", Text),
                C("RoleSummary", "role_summary", Text),
                C("CoreItems", "core_items", Text),
                O("SituationalItems", "situational_items", Text),
                O("Augments", "augments", Text),
                O("SummonerSpells", "summoner_spells", Text),
                O("SkillOrder", "skill_order", Text),
                O("PlaystyleTips", "playstyle_tips", Text),
                O("PositioningTips", "positioning_tips", Text),
                O("Weaknesses", "weaknesses", Text),
                O("SourceUrl", "source_url", Text),
                O("Notes", "notes", Text),
                O("CreatedAt", "created_at", Time),
                O("UpdatedAt", "updated_at", Time)
            }),

        WithId(
            "lol_aram_augment_series",
            "LolAramAugmentSeries",
            "lol_aram_augment_series",
            new[]
            {
                C("Id", "id", BigInt),
                C("SeriesKey", "series_key", Text),
                C("SeriesName", "series_name", Text),
                O("Description", "description", Text),
                O("SetBonusText", "set_bonus_text", Text),
                O("Tags", "tags", Text),
                C("PatchVersion", "patch_version", Text),
                O("SourceUrl", "source_url", Text),
                O("Notes", "notes", Text),
                O("CreatedAt", "created_at", Time),
                O("UpdatedAt", "updated_at", Time)
            }),

        WithId(
            "lol_aram_augments",
            "LolAramAugments",
            "lol_aram_augments",
            new[]
            {
                C("Id", "id", BigInt),
                C("AugmentKey", "augment_key", Text),
                C("Name", "name", Text),
                C("ModeName", "mode_name", Text),
                C("Rarity", "rarity", Text),
                O("Tier", "tier", Text),
                O("SeriesKey", "series_key", Text),
                C("EffectText", "effect_text", Text),
                O("Tags", "tags", Text),
                O("SynergyNotes", "synergy_notes", Text),
                C("PatchVersion", "patch_version", Text),
                O("SourceUrl", "source_url", Text),
                O("Notes", "notes", Text),
                O("CreatedAt", "created_at", Time),
                O("UpdatedAt", "updated_at", Time)
            }),

        WithId(
            "lol_aram_items",
            "LolAramItems",
            "lol_aram_items",
            new[]
            {
                C("Id", "id", BigInt),
                C("ItemKey", "item_key", Text),
                C("Name", "name", Text),
                O("Aliases", "aliases", Text),
                C("ModeName", "mode_name", Text),
                C("EffectText", "effect_text", Text),
                O("Tags", "tags", Text),
                O("SynergyNotes", "synergy_notes", Text),
                C("PatchVersion", "patch_version", Text),
                O("SourceUrl", "source_url", Text),
                O("Notes", "notes", Text),
                O("CreatedAt", "created_at", Time),
                O("UpdatedAt", "updated_at", Time)
            }),

        WithId(
            "lol_aram_synergy_rules",
            "LolAramSynergyRules",
            "lol_aram_synergy_rules",
            new[]
            {
                C("Id", "id", BigInt),
                C("RuleName", "rule_name", Text),
                O("BoostAugmentKey", "boost_augment_key", Text),
                O("SeriesKey", "series_key", Text),
                O("TriggerTags", "trigger_tags", Text),
                O("ChampionTags", "champion_tags", Text),
                O("ItemTags", "item_tags", Text),
                C("ConditionText", "condition_text", Text),
                C("RecommendationText", "recommendation_text", Text),
                C("Priority", "priority", Text),
                C("PatchVersion", "patch_version", Text),
                O("Notes", "notes", Text),
                O("CreatedAt", "created_at", Time),
                O("UpdatedAt", "updated_at", Time)
            })
    };

    private static ColumnMap C(string source, string target, NpgsqlDbType type)
    {
        return new ColumnMap(source, target, type);
    }

    private static ColumnMap O(string source, string target, NpgsqlDbType type)
    {
        return new ColumnMap(source, target, type, Required: false);
    }

    private static TableSpec WithId(string name, string source, string target, IReadOnlyList<ColumnMap> columns)
    {
        return new TableSpec(
            name,
            source,
            target,
            "(id)",
            "[Id]",
            HasIdentity: true,
            columns,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "created_at" });
    }
}

sealed class MigrationOptions
{
    public string SourceConnectionString { get; private init; } = string.Empty;

    public string TargetConnectionString { get; private init; } = string.Empty;

    public bool Apply { get; private init; }

    public bool InitializeSchema { get; private init; }

    public bool ConfirmStaging { get; private init; }

    public HashSet<string> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(TargetConnectionString)
        && (InitializeSchema || !string.IsNullOrWhiteSpace(SourceConnectionString));

    public static MigrationOptions Parse(string[] args)
    {
        var options = new MigrationOptions
        {
            SourceConnectionString = Environment.GetEnvironmentVariable("MIGRATION_SQLSERVER_CONNECTION") ?? string.Empty,
            TargetConnectionString = Environment.GetEnvironmentVariable("MIGRATION_POSTGRES_CONNECTION") ?? string.Empty,
            Apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase),
            InitializeSchema = args.Contains("--initialize-schema", StringComparer.OrdinalIgnoreCase),
            ConfirmStaging = IsTruthy(Environment.GetEnvironmentVariable("MIGRATION_CONFIRM_STAGING"))
                || args.Contains("--confirm-staging", StringComparer.OrdinalIgnoreCase)
        };

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--source" when index + 1 < args.Length:
                    options = options.WithSource(args[++index]);
                    break;
                case "--target" when index + 1 < args.Length:
                    options = options.WithTarget(args[++index]);
                    break;
                case "--tables" when index + 1 < args.Length:
                    foreach (var table in args[++index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        options.Tables.Add(table);
                    }
                    break;
            }
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            Usage:
              dotnet run --project Tools/SupabaseDataMigrator/SupabaseDataMigrator.csproj -- --source "<sqlserver>" --target "<postgres>" [--apply --confirm-staging] [--tables users,equipments]
              dotnet run --project Tools/SupabaseDataMigrator/SupabaseDataMigrator.csproj -- --target "<postgres>" --initialize-schema --confirm-staging

            Environment alternatives:
              MIGRATION_SQLSERVER_CONNECTION
              MIGRATION_POSTGRES_CONNECTION

            Safety:
              Without --apply this tool only counts source rows.
              Apply mode requires --confirm-staging or MIGRATION_CONFIRM_STAGING=true.
              Schema initialization requires --initialize-schema and --confirm-staging.
              Apply mode writes inside a PostgreSQL transaction and rolls back on copy failure.
              Use only against a Supabase staging database first.
            """);
    }

    private MigrationOptions WithSource(string value)
    {
        return new MigrationOptions
        {
            SourceConnectionString = value,
            TargetConnectionString = TargetConnectionString,
            Apply = Apply,
            InitializeSchema = InitializeSchema,
            ConfirmStaging = ConfirmStaging,
            Tables = { }
        }.CopyTablesFrom(this);
    }

    private MigrationOptions WithTarget(string value)
    {
        return new MigrationOptions
        {
            SourceConnectionString = SourceConnectionString,
            TargetConnectionString = value,
            Apply = Apply,
            InitializeSchema = InitializeSchema,
            ConfirmStaging = ConfirmStaging,
            Tables = { }
        }.CopyTablesFrom(this);
    }

    private MigrationOptions CopyTablesFrom(MigrationOptions source)
    {
        foreach (var table in source.Tables)
        {
            Tables.Add(table);
        }

        return this;
    }

    private static bool IsTruthy(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes";
    }
}
