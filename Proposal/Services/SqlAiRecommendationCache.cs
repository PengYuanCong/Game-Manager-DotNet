using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services
{
    public class SqlAiRecommendationCache : IAiRecommendationCache
    {
        private const int MaxCacheEntriesPerUser = 80;
        private const int MaxRecommendationJsonLength = 200_000;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly ISqlConnectionFactory _connectionFactory;

        public SqlAiRecommendationCache(ISqlConnectionFactory connectionFactory)
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
            await EnsureTableAsync(connection, cancellationToken);

            const string selectSql = @"
                SELECT RecommendationJson
                FROM AiRecommendationCache
                WHERE Username = @Username AND CacheKey = @CacheKey";

            await using var selectCommand = new SqlCommand(selectSql, connection);
            selectCommand.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimOrEmpty(username, 256);
            selectCommand.Parameters.Add("@CacheKey", SqlDbType.NVarChar, 64).Value = cacheKey;

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
            await EnsureTableAsync(connection, cancellationToken);

            const string upsertSql = @"
                IF EXISTS (
                    SELECT 1
                    FROM AiRecommendationCache
                    WHERE Username = @Username AND CacheKey = @CacheKey
                )
                BEGIN
                    UPDATE AiRecommendationCache
                    SET RecommendationJson = @RecommendationJson,
                        GameTitle = @GameTitle,
                        CoreChampion = @CoreChampion,
                        CurrentStage = @CurrentStage,
                        Augment = @Augment,
                        AvailableItems = @AvailableItems,
                        Notes = @Notes,
                        LastUsedAt = SYSUTCDATETIME()
                    WHERE Username = @Username AND CacheKey = @CacheKey
                END
                ELSE
                BEGIN
                    INSERT INTO AiRecommendationCache
                        (Username, CacheKey, GameTitle, CoreChampion, CurrentStage, Augment, AvailableItems, Notes, RecommendationJson)
                    VALUES
                        (@Username, @CacheKey, @GameTitle, @CoreChampion, @CurrentStage, @Augment, @AvailableItems, @Notes, @RecommendationJson)
                END";

            await using var command = new SqlCommand(upsertSql, connection);
            AddCommonParameters(command, username, cacheKey, input);
            command.Parameters.Add("@RecommendationJson", SqlDbType.NVarChar, -1).Value = recommendationJson;

            await command.ExecuteNonQueryAsync(cancellationToken);
            await PruneOldEntriesAsync(connection, username, cancellationToken);
        }

        private static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string createSql = @"
                IF OBJECT_ID(N'dbo.AiRecommendationCache', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AiRecommendationCache
                    (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        Username NVARCHAR(256) NOT NULL,
                        CacheKey NVARCHAR(64) NOT NULL,
                        GameTitle NVARCHAR(200) NOT NULL,
                        CoreChampion NVARCHAR(200) NOT NULL,
                        CurrentStage NVARCHAR(200) NULL,
                        Augment NVARCHAR(500) NULL,
                        AvailableItems NVARCHAR(500) NULL,
                        Notes NVARCHAR(1000) NULL,
                        RecommendationJson NVARCHAR(MAX) NOT NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_AiRecommendationCache_CreatedAt DEFAULT SYSUTCDATETIME(),
                        LastUsedAt DATETIME2 NOT NULL CONSTRAINT DF_AiRecommendationCache_LastUsedAt DEFAULT SYSUTCDATETIME(),
                        HitCount INT NOT NULL CONSTRAINT DF_AiRecommendationCache_HitCount DEFAULT 0,
                        CONSTRAINT UQ_AiRecommendationCache_Username_CacheKey UNIQUE (Username, CacheKey)
                    )
                END";

            await using var command = new SqlCommand(createSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task TouchCacheAsync(
            SqlConnection connection,
            string username,
            string cacheKey,
            CancellationToken cancellationToken)
        {
            const string updateSql = @"
                UPDATE AiRecommendationCache
                SET LastUsedAt = SYSUTCDATETIME(),
                    HitCount = HitCount + 1
                WHERE Username = @Username AND CacheKey = @CacheKey";

            await using var command = new SqlCommand(updateSql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimOrEmpty(username, 256);
            command.Parameters.Add("@CacheKey", SqlDbType.NVarChar, 64).Value = cacheKey;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task PruneOldEntriesAsync(
            SqlConnection connection,
            string username,
            CancellationToken cancellationToken)
        {
            const string deleteSql = @"
                ;WITH Ranked AS
                (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY Username ORDER BY LastUsedAt DESC, Id DESC) AS RowNumber
                    FROM dbo.AiRecommendationCache
                    WHERE Username = @Username
                )
                DELETE FROM dbo.AiRecommendationCache
                WHERE Id IN (
                    SELECT Id
                    FROM Ranked
                    WHERE RowNumber > @MaxEntries
                );";

            await using var command = new SqlCommand(deleteSql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimOrEmpty(username, 256);
            command.Parameters.Add("@MaxEntries", SqlDbType.Int).Value = MaxCacheEntriesPerUser;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddCommonParameters(
            SqlCommand command,
            string username,
            string cacheKey,
            AiRecommendationInput input)
        {
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimOrEmpty(username, 256);
            command.Parameters.Add("@CacheKey", SqlDbType.NVarChar, 64).Value = cacheKey;
            command.Parameters.Add("@GameTitle", SqlDbType.NVarChar, 200).Value = TrimOrEmpty(input.GameTitle, 200);
            command.Parameters.Add("@CoreChampion", SqlDbType.NVarChar, 200).Value = TrimOrEmpty(input.CoreChampion, 200);
            command.Parameters.Add("@CurrentStage", SqlDbType.NVarChar, 200).Value = DbValueOrNull(input.CurrentStage, 200);
            command.Parameters.Add("@Augment", SqlDbType.NVarChar, 500).Value = DbValueOrNull(input.Augment, 500);
            command.Parameters.Add("@AvailableItems", SqlDbType.NVarChar, 500).Value = DbValueOrNull(input.AvailableItems, 500);
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(input.Notes, 1000);
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
}
