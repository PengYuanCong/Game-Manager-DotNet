using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresLolAramGuideRepository : ILolAramGuideRepository
{
    private const string GuideColumns = """
        id, champion_key, champion_name, localized_name, mode_name, patch_version,
        role_summary, core_items, situational_items, augments, summoner_spells,
        skill_order, playstyle_tips, positioning_tips, weaknesses, source_url,
        notes, created_at, updated_at
        """;

    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresLolAramGuideRepository(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LolAramGuide>> SearchAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var guides = new List<LolAramGuide>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var hasSearch = !string.IsNullOrWhiteSpace(searchText);
        var sql = $"""
            select {GuideColumns}
            from public.lol_aram_guides
            """;

        if (hasSearch)
        {
            sql += """
                
                where champion_key ilike @search
                   or champion_name ilike @search
                   or localized_name ilike @search
                   or mode_name ilike @search
                """;
        }

        sql += """
            
            order by champion_key, mode_name
            limit 200;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        if (hasSearch)
        {
            command.Parameters.Add("@search", NpgsqlDbType.Varchar, 120).Value = $"%{TrimTo(searchText, 118)}%";
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            guides.Add(ReadGuide(reader));
        }

        return guides;
    }

    public async Task<LolAramGuide?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var sql = $"select {GuideColumns} from public.lol_aram_guides where id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadGuide(reader) : null;
    }

    public async Task CreateAsync(LolAramGuide guide, CancellationToken cancellationToken = default)
    {
        PrepareGuide(guide);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(InsertSql, connection);
        AddGuideParameters(command, guide);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<GuideImportResult> UpsertManyAsync(
        IEnumerable<LolAramGuide> guides,
        CancellationToken cancellationToken = default)
    {
        return await UpsertGuidesAsync(guides, updateAugmentsOnly: false, cancellationToken);
    }

    public async Task<GuideImportResult> UpsertAugmentRecommendationsAsync(
        IEnumerable<LolAramGuide> guides,
        CancellationToken cancellationToken = default)
    {
        return await UpsertGuidesAsync(guides, updateAugmentsOnly: true, cancellationToken);
    }

    public async Task<bool> UpdateAsync(LolAramGuide guide, CancellationToken cancellationToken = default)
    {
        PrepareGuide(guide);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            update public.lol_aram_guides
            set champion_key = @champion_key,
                champion_name = @champion_name,
                localized_name = @localized_name,
                mode_name = @mode_name,
                patch_version = @patch_version,
                role_summary = @role_summary,
                core_items = @core_items,
                situational_items = @situational_items,
                augments = @augments,
                summoner_spells = @summoner_spells,
                skill_order = @skill_order,
                playstyle_tips = @playstyle_tips,
                positioning_tips = @positioning_tips,
                weaknesses = @weaknesses,
                source_url = @source_url,
                notes = @notes
            where id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = guide.Id;
        AddGuideParameters(command, guide);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "delete from public.lol_aram_guides where id = @id;",
            connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<GuideImportResult> UpsertGuidesAsync(
        IEnumerable<LolAramGuide> guides,
        bool updateAugmentsOnly,
        CancellationToken cancellationToken)
    {
        var result = new GuideImportResult();
        var rows = guides.ToList();
        if (rows.Count == 0)
        {
            return result;
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        foreach (var guide in rows)
        {
            PrepareGuide(guide);
            if (string.IsNullOrWhiteSpace(guide.ChampionKey)
                || string.IsNullOrWhiteSpace(guide.ChampionName)
                || string.IsNullOrWhiteSpace(guide.Augments)
                || (!updateAugmentsOnly
                    && (string.IsNullOrWhiteSpace(guide.RoleSummary) || string.IsNullOrWhiteSpace(guide.CoreItems))))
            {
                result.SkippedCount++;
                continue;
            }

            var existed = await ExistsAsync(connection, guide, cancellationToken);
            await using var command = new NpgsqlCommand(
                updateAugmentsOnly ? UpsertAugmentsOnlySql : UpsertSql,
                connection);
            AddGuideParameters(command, guide);
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (existed)
            {
                result.UpdatedCount++;
            }
            else
            {
                result.InsertedCount++;
            }
        }

        return result;
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        LolAramGuide guide,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.lol_aram_guides
                where champion_key = @champion_key and mode_name = @mode_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@champion_key", NpgsqlDbType.Varchar, 100).Value = TrimTo(guide.ChampionKey, 100);
        command.Parameters.Add("@mode_name", NpgsqlDbType.Varchar, 100).Value = TrimTo(guide.ModeName, 100);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private const string InsertSql = """
        insert into public.lol_aram_guides
            (champion_key, champion_name, localized_name, mode_name, patch_version,
             role_summary, core_items, situational_items, augments, summoner_spells,
             skill_order, playstyle_tips, positioning_tips, weaknesses, source_url, notes)
        values
            (@champion_key, @champion_name, @localized_name, @mode_name, @patch_version,
             @role_summary, @core_items, @situational_items, @augments, @summoner_spells,
             @skill_order, @playstyle_tips, @positioning_tips, @weaknesses, @source_url, @notes);
        """;

    private const string UpsertSql = """
        insert into public.lol_aram_guides
            (champion_key, champion_name, localized_name, mode_name, patch_version,
             role_summary, core_items, situational_items, augments, summoner_spells,
             skill_order, playstyle_tips, positioning_tips, weaknesses, source_url, notes)
        values
            (@champion_key, @champion_name, @localized_name, @mode_name, @patch_version,
             @role_summary, @core_items, @situational_items, @augments, @summoner_spells,
             @skill_order, @playstyle_tips, @positioning_tips, @weaknesses, @source_url, @notes)
        on conflict (champion_key, mode_name) do update
        set champion_name = excluded.champion_name,
            localized_name = excluded.localized_name,
            patch_version = excluded.patch_version,
            role_summary = excluded.role_summary,
            core_items = excluded.core_items,
            situational_items = excluded.situational_items,
            augments = excluded.augments,
            summoner_spells = excluded.summoner_spells,
            skill_order = excluded.skill_order,
            playstyle_tips = excluded.playstyle_tips,
            positioning_tips = excluded.positioning_tips,
            weaknesses = excluded.weaknesses,
            source_url = excluded.source_url,
            notes = excluded.notes;
        """;

    private const string UpsertAugmentsOnlySql = """
        insert into public.lol_aram_guides
            (champion_key, champion_name, localized_name, mode_name, patch_version,
             role_summary, core_items, situational_items, augments, summoner_spells,
             skill_order, playstyle_tips, positioning_tips, weaknesses, source_url, notes)
        values
            (@champion_key, @champion_name, @localized_name, @mode_name, @patch_version,
             @role_summary, @core_items, @situational_items, @augments, @summoner_spells,
             @skill_order, @playstyle_tips, @positioning_tips, @weaknesses, @source_url, @notes)
        on conflict (champion_key, mode_name) do update
        set champion_name = excluded.champion_name,
            localized_name = excluded.localized_name,
            patch_version = excluded.patch_version,
            augments = excluded.augments,
            source_url = excluded.source_url,
            notes = excluded.notes;
        """;

    private static void AddGuideParameters(NpgsqlCommand command, LolAramGuide guide)
    {
        command.Parameters.Add("@champion_key", NpgsqlDbType.Varchar, 100).Value = TrimTo(guide.ChampionKey, 100);
        command.Parameters.Add("@champion_name", NpgsqlDbType.Varchar, 100).Value = TrimTo(guide.ChampionName, 100);
        command.Parameters.Add("@localized_name", NpgsqlDbType.Varchar, 100).Value = DbValueOrNull(guide.LocalizedName, 100);
        command.Parameters.Add("@mode_name", NpgsqlDbType.Varchar, 100).Value = TrimTo(guide.ModeName, 100);
        command.Parameters.Add("@patch_version", NpgsqlDbType.Varchar, 50).Value = TrimTo(guide.PatchVersion, 50);
        command.Parameters.Add("@role_summary", NpgsqlDbType.Varchar, 500).Value = TrimTo(guide.RoleSummary, 500);
        command.Parameters.Add("@core_items", NpgsqlDbType.Varchar, 1000).Value = TrimTo(guide.CoreItems, 1000);
        command.Parameters.Add("@situational_items", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(guide.SituationalItems, 1000);
        command.Parameters.Add("@augments", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(guide.Augments, 1000);
        command.Parameters.Add("@summoner_spells", NpgsqlDbType.Varchar, 500).Value = DbValueOrNull(guide.SummonerSpells, 500);
        command.Parameters.Add("@skill_order", NpgsqlDbType.Varchar, 500).Value = DbValueOrNull(guide.SkillOrder, 500);
        command.Parameters.Add("@playstyle_tips", NpgsqlDbType.Varchar, 1200).Value = DbValueOrNull(guide.PlaystyleTips, 1200);
        command.Parameters.Add("@positioning_tips", NpgsqlDbType.Varchar, 1200).Value = DbValueOrNull(guide.PositioningTips, 1200);
        command.Parameters.Add("@weaknesses", NpgsqlDbType.Varchar, 1200).Value = DbValueOrNull(guide.Weaknesses, 1200);
        command.Parameters.Add("@source_url", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(guide.SourceUrl, 1000);
        command.Parameters.Add("@notes", NpgsqlDbType.Varchar, 1200).Value = DbValueOrNull(guide.Notes, 1200);
    }

    private static void PrepareGuide(LolAramGuide guide)
    {
        guide.ChampionKey = NormalizeChampionKey(guide.ChampionKey);
        guide.ModeName = string.IsNullOrWhiteSpace(guide.ModeName) ? "ARAM Mayhem" : guide.ModeName.Trim();
        guide.PatchVersion = string.IsNullOrWhiteSpace(guide.PatchVersion) ? "manual" : guide.PatchVersion.Trim();
    }

    private static LolAramGuide ReadGuide(NpgsqlDataReader reader)
    {
        return new LolAramGuide
        {
            Id = ReadInt32(reader, "id"),
            ChampionKey = ReadString(reader, "champion_key", string.Empty),
            ChampionName = ReadString(reader, "champion_name", string.Empty),
            LocalizedName = ReadNullableString(reader, "localized_name"),
            ModeName = ReadString(reader, "mode_name", string.Empty),
            PatchVersion = ReadString(reader, "patch_version", string.Empty),
            RoleSummary = ReadString(reader, "role_summary", string.Empty),
            CoreItems = ReadString(reader, "core_items", string.Empty),
            SituationalItems = ReadNullableString(reader, "situational_items"),
            Augments = ReadNullableString(reader, "augments"),
            SummonerSpells = ReadNullableString(reader, "summoner_spells"),
            SkillOrder = ReadNullableString(reader, "skill_order"),
            PlaystyleTips = ReadNullableString(reader, "playstyle_tips"),
            PositioningTips = ReadNullableString(reader, "positioning_tips"),
            Weaknesses = ReadNullableString(reader, "weaknesses"),
            SourceUrl = ReadNullableString(reader, "source_url"),
            Notes = ReadNullableString(reader, "notes"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
        };
    }

    private static string NormalizeChampionKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static object DbValueOrNull(string? value, int maxLength)
    {
        return TrimTo(value, maxLength) is { Length: > 0 } trimmed ? trimmed : DBNull.Value;
    }

    private static string TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string ReadString(NpgsqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int ReadInt32(NpgsqlDataReader reader, string name)
    {
        return Convert.ToInt32(reader[name]);
    }

    private static DateTime ReadDateTime(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value is DateTime dateTime ? dateTime : Convert.ToDateTime(value);
    }
}
