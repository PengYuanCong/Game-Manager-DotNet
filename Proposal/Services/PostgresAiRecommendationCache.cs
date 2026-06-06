using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresAiRecommendationCache : IAiRecommendationCache
{
    private const int MaxCacheEntriesPerUser = 80;
    private const int MaxRecommendationJsonLength = 200_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresAiRecommendationCache(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<GameRecommendation?> GetAsync(
        string username,
        AiRecommendationInput input,
        string cacheScope,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = AiRecommendationCacheKey.Create(input, cacheScope);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string selectSql = """
            select recommendation_json::text
            from public.ai_recommendation_cache
            where username = @username and cache_key = @cache_key;
            """;

        await using var selectCommand = new NpgsqlCommand(selectSql, connection);
        selectCommand.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimOrEmpty(username, 256);
        selectCommand.Parameters.Add("@cache_key", NpgsqlDbType.Varchar, 64).Value = cacheKey;

        var recommendationJson = await selectCommand.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(recommendationJson))
        {
            return null;
        }

        await TouchCacheAsync(connection, username, cacheKey, cancellationToken);

        var recommendation = JsonSerializer.Deserialize<GameRecommendation>(recommendationJson, JsonOptions);
        if (recommendation != null)
        {
            recommendation.CacheKey = cacheKey;
        }

        return recommendation;
    }

    public async Task SaveAsync(
        string username,
        AiRecommendationInput input,
        string cacheScope,
        GameRecommendation recommendation,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = AiRecommendationCacheKey.Create(input, cacheScope);
        recommendation.CacheKey = cacheKey;
        var recommendationJson = JsonSerializer.Serialize(recommendation, JsonOptions);
        if (recommendationJson.Length > MaxRecommendationJsonLength)
        {
            return;
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string upsertSql = """
            insert into public.ai_recommendation_cache
                (username, cache_key, game_title, core_champion, current_stage, augment, available_items, notes, recommendation_json)
            values
                (@username, @cache_key, @game_title, @core_champion, @current_stage, @augment, @available_items, @notes, @recommendation_json)
            on conflict (username, cache_key) do update
            set recommendation_json = excluded.recommendation_json,
                game_title = excluded.game_title,
                core_champion = excluded.core_champion,
                current_stage = excluded.current_stage,
                augment = excluded.augment,
                available_items = excluded.available_items,
                notes = excluded.notes,
                last_used_at = now();
            """;

        await using var command = new NpgsqlCommand(upsertSql, connection);
        AddCommonParameters(command, username, cacheKey, input);
        command.Parameters.Add("@recommendation_json", NpgsqlDbType.Jsonb).Value = recommendationJson;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await PruneOldEntriesAsync(connection, username, cancellationToken);
    }

    private static async Task TouchCacheAsync(
        NpgsqlConnection connection,
        string username,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update public.ai_recommendation_cache
            set last_used_at = now(),
                hit_count = hit_count + 1
            where username = @username and cache_key = @cache_key;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimOrEmpty(username, 256);
        command.Parameters.Add("@cache_key", NpgsqlDbType.Varchar, 64).Value = cacheKey;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PruneOldEntriesAsync(
        NpgsqlConnection connection,
        string username,
        CancellationToken cancellationToken)
    {
        const string sql = """
            delete from public.ai_recommendation_cache
            where id in (
                select id
                from (
                    select id,
                           row_number() over (partition by username order by last_used_at desc, id desc) as row_number
                    from public.ai_recommendation_cache
                    where username = @username
                ) ranked
                where ranked.row_number > @max_entries
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimOrEmpty(username, 256);
        command.Parameters.Add("@max_entries", NpgsqlDbType.Integer).Value = MaxCacheEntriesPerUser;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(
        NpgsqlCommand command,
        string username,
        string cacheKey,
        AiRecommendationInput input)
    {
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimOrEmpty(username, 256);
        command.Parameters.Add("@cache_key", NpgsqlDbType.Varchar, 64).Value = cacheKey;
        command.Parameters.Add("@game_title", NpgsqlDbType.Varchar, 200).Value = TrimOrEmpty(input.GameTitle, 200);
        command.Parameters.Add("@core_champion", NpgsqlDbType.Varchar, 200).Value = TrimOrEmpty(input.CoreChampion, 200);
        command.Parameters.Add("@current_stage", NpgsqlDbType.Varchar, 200).Value = DbValueOrNull(input.CurrentStage, 200);
        command.Parameters.Add("@augment", NpgsqlDbType.Varchar, 500).Value = DbValueOrNull(input.Augment, 500);
        command.Parameters.Add("@available_items", NpgsqlDbType.Varchar, 500).Value = DbValueOrNull(input.AvailableItems, 500);
        command.Parameters.Add("@notes", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(input.Notes, 1000);
    }

    private static string TrimOrEmpty(string? value, int maxLength)
    {
        return TrimTo(value, maxLength) ?? string.Empty;
    }

    private static object DbValueOrNull(string? value, int maxLength)
    {
        return TrimTo(value, maxLength) is { Length: > 0 } trimmed ? trimmed : DBNull.Value;
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
