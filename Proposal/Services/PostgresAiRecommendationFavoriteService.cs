using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresAiRecommendationFavoriteService : IAiRecommendationFavoriteService
{
    private const int MaxFavoritesPerUser = 40;
    private const int MaxSerializedPayloadLength = 200_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresAiRecommendationFavoriteService(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> SaveAsync(
        string username,
        AiRecommendationInput input,
        GameRecommendation recommendation,
        CancellationToken cancellationToken = default)
    {
        return await SaveInternalAsync(username, input, recommendation, adopted: false, cancellationToken);
    }

    public async Task<int> AdoptAsync(
        string username,
        AiRecommendationInput input,
        GameRecommendation recommendation,
        CancellationToken cancellationToken = default)
    {
        return await SaveInternalAsync(username, input, recommendation, adopted: true, cancellationToken);
    }

    public async Task<AiRecommendationFavorite?> GetAsync(
        string username,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select *
            from public.ai_recommendation_favorites
            where username = @username and id = @id
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimTo(username, 256);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFavorite(reader) : null;
    }

    public async Task<IReadOnlyList<AiRecommendationFavorite>> GetRecentAsync(
        string username,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await QueryRecentAsync(username, Math.Clamp(limit, 1, MaxFavoritesPerUser), cancellationToken);
    }

    public async Task<IReadOnlyList<AiRecommendationFavorite>> FindRelevantAsync(
        string username,
        AiRecommendationInput input,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var recent = await QueryRecentAsync(username, 50, cancellationToken);
        return recent
            .Select(favorite => new { Favorite = favorite, Score = ScoreFavorite(favorite, input) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Favorite.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 5))
            .Select(item => item.Favorite)
            .ToList();
    }

    public async Task<IReadOnlyList<AiRecommendationFavorite>> FindCommunityAcceptedAsync(
        AiRecommendationInput input,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var recent = await QueryAcceptedAsync(Math.Clamp(limit * 6, 12, 80), cancellationToken);
        return recent
            .Select(favorite => new { Favorite = favorite, Score = ScoreFavorite(favorite, input) + Math.Min(favorite.AdoptedCount, 10) })
            .Where(item => item.Favorite.AdoptedCount > 0 && item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Favorite.AdoptedCount)
            .ThenByDescending(item => item.Favorite.LastAdoptedAt ?? item.Favorite.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 5))
            .Select(item => item.Favorite)
            .ToList();
    }

    private async Task<int> SaveInternalAsync(
        string username,
        AiRecommendationInput input,
        GameRecommendation recommendation,
        bool adopted,
        CancellationToken cancellationToken)
    {
        var normalizedInput = NormalizeInput(input);
        var inputHash = AiRecommendationCacheKey.Create(normalizedInput, "favorite:v1");
        var inputJson = JsonSerializer.Serialize(normalizedInput, JsonOptions);
        var recommendationJson = JsonSerializer.Serialize(recommendation, JsonOptions);
        EnsurePayloadSize(inputJson, "input");
        EnsurePayloadSize(recommendationJson, "recommendation");

        var title = BuildTitle(normalizedInput, recommendation);
        var summary = TrimTo(recommendation.Summary, 500);
        var itemNames = string.Join(", ", recommendation.RecommendedItems.Select(item => item.ItemName).Where(NotBlank).Take(6));
        var augmentNames = string.Join(", ", recommendation.RecommendedAugments.Select(augment => augment.AugmentName).Where(NotBlank).Take(8));

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            insert into public.ai_recommendation_favorites
                (username, input_hash, title, game_title, core_champion, current_stage, augment, available_items, notes,
                 summary, recommended_items, recommended_augments, input_json, recommendation_json, adopted_count, last_adopted_at)
            values
                (@username, @input_hash, @title, @game_title, @core_champion, @current_stage, @augment, @available_items, @notes,
                 @summary, @recommended_items, @recommended_augments, @input_json, @recommendation_json,
                 @adopt_increment, case when @adopt_increment = 1 then now() else null end)
            on conflict (username, input_hash) do update
            set title = excluded.title,
                game_title = excluded.game_title,
                core_champion = excluded.core_champion,
                current_stage = excluded.current_stage,
                augment = excluded.augment,
                available_items = excluded.available_items,
                notes = excluded.notes,
                summary = excluded.summary,
                recommended_items = excluded.recommended_items,
                recommended_augments = excluded.recommended_augments,
                input_json = excluded.input_json,
                recommendation_json = excluded.recommendation_json,
                adopted_count = public.ai_recommendation_favorites.adopted_count + @adopt_increment,
                last_adopted_at = case when @adopt_increment = 1 then now() else public.ai_recommendation_favorites.last_adopted_at end,
                updated_at = now()
            returning id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddSaveParameters(command, username, inputHash, normalizedInput, title, summary, itemNames, augmentNames, inputJson, recommendationJson);
        command.Parameters.Add("@adopt_increment", NpgsqlDbType.Integer).Value = adopted ? 1 : 0;

        var savedId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await PruneOldEntriesAsync(connection, username, cancellationToken);
        return savedId;
    }

    private async Task<IReadOnlyList<AiRecommendationFavorite>> QueryRecentAsync(
        string username,
        int limit,
        CancellationToken cancellationToken)
    {
        var favorites = new List<AiRecommendationFavorite>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select *
            from public.ai_recommendation_favorites
            where username = @username
            order by updated_at desc, id desc
            limit @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimTo(username, 256);
        command.Parameters.Add("@limit", NpgsqlDbType.Integer).Value = Math.Clamp(limit, 1, 50);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            favorites.Add(ReadFavorite(reader));
        }

        return favorites;
    }

    private async Task<IReadOnlyList<AiRecommendationFavorite>> QueryAcceptedAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var favorites = new List<AiRecommendationFavorite>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select *
            from public.ai_recommendation_favorites
            where adopted_count > 0
            order by adopted_count desc, last_adopted_at desc, updated_at desc
            limit @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@limit", NpgsqlDbType.Integer).Value = Math.Clamp(limit, 1, 80);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            favorites.Add(ReadFavorite(reader));
        }

        return favorites;
    }

    private static async Task PruneOldEntriesAsync(
        NpgsqlConnection connection,
        string username,
        CancellationToken cancellationToken)
    {
        const string sql = """
            delete from public.ai_recommendation_favorites
            where id in (
                select id
                from (
                    select id,
                           row_number() over (partition by username order by updated_at desc, id desc) as row_number
                    from public.ai_recommendation_favorites
                    where username = @username
                ) ranked
                where ranked.row_number > @max_favorites
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimTo(username, 256);
        command.Parameters.Add("@max_favorites", NpgsqlDbType.Integer).Value = MaxFavoritesPerUser;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddSaveParameters(
        NpgsqlCommand command,
        string username,
        string inputHash,
        AiRecommendationInput input,
        string title,
        string summary,
        string itemNames,
        string augmentNames,
        string inputJson,
        string recommendationJson)
    {
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 256).Value = TrimTo(username, 256);
        command.Parameters.Add("@input_hash", NpgsqlDbType.Varchar, 64).Value = inputHash;
        command.Parameters.Add("@title", NpgsqlDbType.Varchar, 200).Value = title;
        command.Parameters.Add("@game_title", NpgsqlDbType.Varchar, 200).Value = TrimTo(input.GameTitle, 200);
        command.Parameters.Add("@core_champion", NpgsqlDbType.Varchar, 200).Value = TrimTo(input.CoreChampion, 200);
        command.Parameters.Add("@current_stage", NpgsqlDbType.Varchar, 200).Value = DbValueOrNull(input.CurrentStage, 200);
        command.Parameters.Add("@augment", NpgsqlDbType.Varchar, 500).Value = DbValueOrNull(input.Augment, 500);
        command.Parameters.Add("@available_items", NpgsqlDbType.Varchar, 500).Value = DbValueOrNull(input.AvailableItems, 500);
        command.Parameters.Add("@notes", NpgsqlDbType.Varchar, 1000).Value = DbValueOrNull(input.Notes, 1000);
        command.Parameters.Add("@summary", NpgsqlDbType.Varchar, 500).Value = summary;
        command.Parameters.Add("@recommended_items", NpgsqlDbType.Varchar, 500).Value = itemNames;
        command.Parameters.Add("@recommended_augments", NpgsqlDbType.Varchar, 700).Value = augmentNames;
        command.Parameters.Add("@input_json", NpgsqlDbType.Jsonb).Value = inputJson;
        command.Parameters.Add("@recommendation_json", NpgsqlDbType.Jsonb).Value = recommendationJson;
    }

    private static AiRecommendationFavorite ReadFavorite(NpgsqlDataReader reader)
    {
        return new AiRecommendationFavorite
        {
            Id = Convert.ToInt32(reader["id"]),
            Username = ReadString(reader, "username"),
            Title = ReadString(reader, "title"),
            GameTitle = ReadString(reader, "game_title"),
            CoreChampion = ReadString(reader, "core_champion"),
            CurrentStage = ReadNullableString(reader, "current_stage"),
            Augment = ReadNullableString(reader, "augment"),
            AvailableItems = ReadNullableString(reader, "available_items"),
            Notes = ReadNullableString(reader, "notes"),
            Summary = ReadString(reader, "summary"),
            RecommendedItems = ReadString(reader, "recommended_items"),
            RecommendedAugments = ReadString(reader, "recommended_augments"),
            InputJson = ReadString(reader, "input_json"),
            RecommendationJson = ReadString(reader, "recommendation_json"),
            AdoptedCount = ReadInt32(reader, "adopted_count"),
            LastAdoptedAt = ReadNullableDateTime(reader, "last_adopted_at"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
        };
    }

    private static AiRecommendationInput NormalizeInput(AiRecommendationInput input)
    {
        return new AiRecommendationInput
        {
            GameTitle = TrimOrDefault(input.GameTitle, "英雄聯盟 隨機單中大亂鬥"),
            CoreChampion = TrimOrEmpty(input.CoreChampion),
            CurrentStage = TrimOrNull(input.CurrentStage),
            Augment = TrimOrNull(input.Augment),
            AvailableItems = TrimOrNull(input.AvailableItems),
            Notes = TrimOrNull(input.Notes)
        };
    }

    private static int ScoreFavorite(AiRecommendationFavorite favorite, AiRecommendationInput input)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(input.CoreChampion)
            && ContainsNormalized(favorite.CoreChampion, input.CoreChampion))
        {
            score += 8;
        }

        score += ScoreTerms(favorite.Augment, input.Augment, 4);
        score += ScoreTerms(favorite.RecommendedAugments, input.Augment, 3);
        score += ScoreTerms(favorite.AvailableItems, input.AvailableItems, 3);
        score += ScoreTerms(favorite.RecommendedItems, input.AvailableItems, 2);
        score += ScoreTerms(favorite.CurrentStage, input.CurrentStage, 1);
        score += ScoreTerms(favorite.Notes, input.Notes, 1);

        return score;
    }

    private static int ScoreTerms(string? source, string? query, int weight)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        return SplitTerms(query)
            .Where(term => ContainsNormalized(source, term))
            .Take(4)
            .Count() * weight;
    }

    private static IEnumerable<string> SplitTerms(string value)
    {
        return value.Split(
            new[] { ' ', ',', '，', ';', '；', '/', '、', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool ContainsNormalized(string source, string query)
    {
        return NormalizeForCompare(source).Contains(NormalizeForCompare(query), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForCompare(string value)
    {
        return value.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static string BuildTitle(AiRecommendationInput input, GameRecommendation recommendation)
    {
        var champion = !string.IsNullOrWhiteSpace(input.CoreChampion)
            ? input.CoreChampion
            : recommendation.CoreChampion;
        var stage = string.IsNullOrWhiteSpace(input.CurrentStage) ? string.Empty : $" {input.CurrentStage}";
        return TrimTo($"{champion}{stage} 推薦", 200);
    }

    private static void EnsurePayloadSize(string payload, string label)
    {
        if (payload.Length > MaxSerializedPayloadLength)
        {
            throw new InvalidOperationException($"{label} payload is too large to save safely.");
        }
    }

    private static bool NotBlank(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string TrimOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string TrimOrEmpty(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private static object DbValueOrNull(string? value, int maxLength)
    {
        return TrimTo(value, maxLength) is { Length: > 0 } trimmed ? trimmed : DBNull.Value;
    }

    private static string ReadString(NpgsqlDataReader reader, string name)
    {
        return reader[name] == DBNull.Value ? string.Empty : reader[name].ToString() ?? string.Empty;
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string name)
    {
        return reader[name] == DBNull.Value ? null : reader[name].ToString();
    }

    private static DateTime ReadDateTime(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            _ => DateTime.UtcNow
        };
    }

    private static DateTime? ReadNullableDateTime(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value switch
        {
            DBNull => null,
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            _ => null
        };
    }

    private static int ReadInt32(NpgsqlDataReader reader, string name)
    {
        return reader[name] == DBNull.Value ? 0 : Convert.ToInt32(reader[name]);
    }
}
