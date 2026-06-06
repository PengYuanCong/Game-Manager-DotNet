using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresLolAramAugmentRepository : ILolAramAugmentRepository
{
    private const string AugmentColumns = """
        id, augment_key, name, mode_name, rarity, tier, series_key, effect_text,
        tags, synergy_notes, patch_version, source_url, notes, created_at, updated_at
        """;

    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresLolAramAugmentRepository(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LolAramAugment>> SearchAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var augments = new List<LolAramAugment>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var hasSearch = !string.IsNullOrWhiteSpace(searchText);
        var sql = $"""
            select {AugmentColumns}
            from public.lol_aram_augments
            """;

        if (hasSearch)
        {
            sql += """
                
                where augment_key ilike @search
                   or name ilike @search
                   or rarity ilike @search
                   or tier ilike @search
                   or series_key ilike @search
                   or tags ilike @search
                """;
        }

        sql += """
            
            order by updated_at desc, name
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
            augments.Add(ReadAugment(reader));
        }

        return augments;
    }

    public async Task<LolAramAugment?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var sql = $"select {AugmentColumns} from public.lol_aram_augments where id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAugment(reader) : null;
    }

    public async Task CreateAsync(LolAramAugment augment, CancellationToken cancellationToken = default)
    {
        PrepareAugment(augment);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(InsertSql, connection);
        AddAugmentParameters(command, augment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(LolAramAugment augment, CancellationToken cancellationToken = default)
    {
        PrepareAugment(augment);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            update public.lol_aram_augments
            set augment_key = @augment_key,
                name = @name,
                mode_name = @mode_name,
                rarity = @rarity,
                tier = @tier,
                series_key = @series_key,
                effect_text = @effect_text,
                tags = @tags,
                synergy_notes = @synergy_notes,
                patch_version = @patch_version,
                source_url = @source_url,
                notes = @notes
            where id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = augment.Id;
        AddAugmentParameters(command, augment);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "delete from public.lol_aram_augments where id = @id;",
            connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<AugmentImportResult> UpsertManyAsync(
        IEnumerable<LolAramAugment> augments,
        CancellationToken cancellationToken = default)
    {
        var result = new AugmentImportResult();
        var rows = augments.ToList();
        if (rows.Count == 0)
        {
            return result;
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        foreach (var augment in rows)
        {
            PrepareAugment(augment);
            if (string.IsNullOrWhiteSpace(augment.AugmentKey)
                || string.IsNullOrWhiteSpace(augment.Name)
                || string.IsNullOrWhiteSpace(augment.EffectText))
            {
                result.SkippedCount++;
                continue;
            }

            var existed = await ExistsAsync(connection, augment, cancellationToken);
            await using var command = new NpgsqlCommand(UpsertSql, connection);
            AddAugmentParameters(command, augment);
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
        LolAramAugment augment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.lol_aram_augments
                where mode_name = @mode_name
                  and (augment_key = @augment_key or name = @name)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@augment_key", NpgsqlDbType.Varchar, 100).Value = TrimTo(augment.AugmentKey, 100);
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, 100).Value = TrimTo(augment.Name, 100);
        command.Parameters.Add("@mode_name", NpgsqlDbType.Varchar, 100).Value = TrimTo(augment.ModeName, 100);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private const string InsertSql = """
        insert into public.lol_aram_augments
            (augment_key, name, mode_name, rarity, tier, series_key, effect_text, tags,
             synergy_notes, patch_version, source_url, notes)
        values
            (@augment_key, @name, @mode_name, @rarity, @tier, @series_key, @effect_text, @tags,
             @synergy_notes, @patch_version, @source_url, @notes);
        """;

    private const string UpsertSql = """
        insert into public.lol_aram_augments
            (augment_key, name, mode_name, rarity, tier, series_key, effect_text, tags,
             synergy_notes, patch_version, source_url, notes)
        values
            (@augment_key, @name, @mode_name, @rarity, @tier, @series_key, @effect_text, @tags,
             @synergy_notes, @patch_version, @source_url, @notes)
        on conflict (augment_key, mode_name) do update
        set name = excluded.name,
            rarity = excluded.rarity,
            tier = excluded.tier,
            series_key = excluded.series_key,
            effect_text = excluded.effect_text,
            tags = excluded.tags,
            synergy_notes = excluded.synergy_notes,
            patch_version = excluded.patch_version,
            source_url = excluded.source_url,
            notes = excluded.notes;
        """;

    private static void AddAugmentParameters(NpgsqlCommand command, LolAramAugment augment)
    {
        command.Parameters.Add("@augment_key", NpgsqlDbType.Varchar, 100).Value = TrimTo(augment.AugmentKey, 100);
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, 100).Value = TrimTo(augment.Name, 100);
        command.Parameters.Add("@mode_name", NpgsqlDbType.Varchar, 100).Value = TrimTo(augment.ModeName, 100);
        command.Parameters.Add("@rarity", NpgsqlDbType.Varchar, 50).Value = TrimTo(augment.Rarity, 50);
        command.Parameters.Add("@tier", NpgsqlDbType.Varchar, 20).Value = DbValueOrNull(augment.Tier, 20);
        command.Parameters.Add("@series_key", NpgsqlDbType.Varchar, 100).Value = DbValueOrNull(augment.SeriesKey, 100);
        command.Parameters.Add("@effect_text", NpgsqlDbType.Varchar, 1200).Value = TrimTo(augment.EffectText, 1200);
        command.Parameters.Add("@tags", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(augment.Tags, 1000);
        command.Parameters.Add("@synergy_notes", NpgsqlDbType.Varchar, 1200).Value = DbValueOrNull(augment.SynergyNotes, 1200);
        command.Parameters.Add("@patch_version", NpgsqlDbType.Varchar, 50).Value = TrimTo(augment.PatchVersion, 50);
        command.Parameters.Add("@source_url", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(augment.SourceUrl, 1000);
        command.Parameters.Add("@notes", NpgsqlDbType.Varchar, 1200).Value = DbValueOrNull(augment.Notes, 1200);
    }

    private static void PrepareAugment(LolAramAugment augment)
    {
        augment.AugmentKey = NormalizeKey(augment.AugmentKey);
        augment.ModeName = string.IsNullOrWhiteSpace(augment.ModeName) ? "ARAM Mayhem" : augment.ModeName.Trim();
        augment.Rarity = string.IsNullOrWhiteSpace(augment.Rarity) ? "gold" : augment.Rarity.Trim().ToLowerInvariant();
        augment.PatchVersion = string.IsNullOrWhiteSpace(augment.PatchVersion) ? "manual" : augment.PatchVersion.Trim();
        augment.SeriesKey = LolAramAugmentTagNormalizer.NormalizeSeriesKey(augment.SeriesKey);
        augment.Tags = LolAramAugmentTagNormalizer.NormalizeTags(augment.Tags, augment.EffectText, augment.SeriesKey);
    }

    private static LolAramAugment ReadAugment(NpgsqlDataReader reader)
    {
        var augment = new LolAramAugment
        {
            Id = ReadInt32(reader, "id"),
            AugmentKey = ReadString(reader, "augment_key", string.Empty),
            Name = ReadString(reader, "name", string.Empty),
            ModeName = ReadString(reader, "mode_name", string.Empty),
            Rarity = ReadString(reader, "rarity", string.Empty),
            Tier = ReadNullableString(reader, "tier"),
            SeriesKey = LolAramAugmentTagNormalizer.NormalizeSeriesKey(ReadNullableString(reader, "series_key")),
            EffectText = ReadString(reader, "effect_text", string.Empty),
            Tags = ReadNullableString(reader, "tags"),
            SynergyNotes = ReadNullableString(reader, "synergy_notes"),
            PatchVersion = ReadString(reader, "patch_version", string.Empty),
            SourceUrl = ReadNullableString(reader, "source_url"),
            Notes = ReadNullableString(reader, "notes"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
        };

        augment.Tags = LolAramAugmentTagNormalizer.NormalizeTags(augment.Tags, augment.EffectText, augment.SeriesKey);
        return augment;
    }

    private static string NormalizeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", "_").ToLowerInvariant();
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
