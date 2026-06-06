using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services
{
    public class SqlAiRecommendationFavoriteService : IAiRecommendationFavoriteService
    {
        private const int MaxFavoritesPerUser = 40;
        private const int MaxSerializedPayloadLength = 200_000;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly ISqlConnectionFactory _connectionFactory;

        public SqlAiRecommendationFavoriteService(ISqlConnectionFactory connectionFactory)
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
            EnsurePayloadSize(inputJson, "推薦輸入");
            EnsurePayloadSize(recommendationJson, "推薦結果");
            var title = BuildTitle(normalizedInput, recommendation);
            var summary = TrimTo(recommendation.Summary, 500);
            var itemNames = string.Join("、", recommendation.RecommendedItems.Select(item => item.ItemName).Where(NotBlank).Take(6));
            var augmentNames = string.Join("、", recommendation.RecommendedAugments.Select(augment => augment.AugmentName).Where(NotBlank).Take(8));

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureTableAsync(connection, cancellationToken);

            const string sql = @"
                DECLARE @SavedId INT;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.AiRecommendationFavorites
                    WHERE Username = @Username AND InputHash = @InputHash
                )
                BEGIN
                    UPDATE dbo.AiRecommendationFavorites
                    SET Title = @Title,
                        GameTitle = @GameTitle,
                        CoreChampion = @CoreChampion,
                        CurrentStage = @CurrentStage,
                        Augment = @Augment,
                        AvailableItems = @AvailableItems,
                        Notes = @Notes,
                        Summary = @Summary,
                        RecommendedItems = @RecommendedItems,
                        RecommendedAugments = @RecommendedAugments,
                        InputJson = @InputJson,
                        RecommendationJson = @RecommendationJson,
                        AdoptedCount = AdoptedCount + @AdoptIncrement,
                        LastAdoptedAt = CASE WHEN @AdoptIncrement = 1 THEN SYSUTCDATETIME() ELSE LastAdoptedAt END,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE Username = @Username AND InputHash = @InputHash;

                    SELECT @SavedId = Id
                    FROM dbo.AiRecommendationFavorites
                    WHERE Username = @Username AND InputHash = @InputHash;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.AiRecommendationFavorites
                        (Username, InputHash, Title, GameTitle, CoreChampion, CurrentStage, Augment, AvailableItems, Notes,
                         Summary, RecommendedItems, RecommendedAugments, InputJson, RecommendationJson, AdoptedCount, LastAdoptedAt)
                    VALUES
                        (@Username, @InputHash, @Title, @GameTitle, @CoreChampion, @CurrentStage, @Augment, @AvailableItems, @Notes,
                         @Summary, @RecommendedItems, @RecommendedAugments, @InputJson, @RecommendationJson,
                         @AdoptIncrement, CASE WHEN @AdoptIncrement = 1 THEN SYSUTCDATETIME() ELSE NULL END);

                    SELECT @SavedId = CONVERT(INT, SCOPE_IDENTITY());
                END

                ;WITH Ranked AS
                (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY Username ORDER BY UpdatedAt DESC, Id DESC) AS RowNumber
                    FROM dbo.AiRecommendationFavorites
                    WHERE Username = @Username
                )
                DELETE FROM dbo.AiRecommendationFavorites
                WHERE Id IN (
                    SELECT Id
                    FROM Ranked
                    WHERE RowNumber > @MaxFavorites
                );

                SELECT @SavedId;";

            await using var command = new SqlCommand(sql, connection);
            AddSaveParameters(
                command,
                username,
                inputHash,
                normalizedInput,
                title,
                summary,
                itemNames,
                augmentNames,
                inputJson,
                recommendationJson);
            command.Parameters.Add("@MaxFavorites", SqlDbType.Int).Value = MaxFavoritesPerUser;
            command.Parameters.Add("@AdoptIncrement", SqlDbType.Int).Value = adopted ? 1 : 0;

            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        public async Task<AiRecommendationFavorite?> GetAsync(
            string username,
            int id,
            CancellationToken cancellationToken = default)
        {
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureTableAsync(connection, cancellationToken);

            const string sql = @"
                SELECT TOP 1 *
                FROM dbo.AiRecommendationFavorites
                WHERE Username = @Username AND Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimTo(username, 256);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

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
            var scored = recent
                .Select(favorite => new { Favorite = favorite, Score = ScoreFavorite(favorite, input) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Favorite.UpdatedAt)
                .Take(Math.Clamp(limit, 1, 5))
                .Select(item => item.Favorite)
                .ToList();

            return scored;
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

        private async Task<IReadOnlyList<AiRecommendationFavorite>> QueryRecentAsync(
            string username,
            int limit,
            CancellationToken cancellationToken)
        {
            var favorites = new List<AiRecommendationFavorite>();

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureTableAsync(connection, cancellationToken);

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM dbo.AiRecommendationFavorites
                WHERE Username = @Username
                ORDER BY UpdatedAt DESC, Id DESC";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimTo(username, 256);
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(limit, 1, 50);

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
            await EnsureTableAsync(connection, cancellationToken);

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM dbo.AiRecommendationFavorites
                WHERE AdoptedCount > 0
                ORDER BY AdoptedCount DESC, LastAdoptedAt DESC, UpdatedAt DESC";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(limit, 1, 80);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                favorites.Add(ReadFavorite(reader));
            }

            return favorites;
        }

        private static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string sql = @"
                IF OBJECT_ID(N'dbo.AiRecommendationFavorites', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AiRecommendationFavorites
                    (
                        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiRecommendationFavorites PRIMARY KEY,
                        Username NVARCHAR(256) NOT NULL,
                        InputHash NVARCHAR(64) NOT NULL,
                        Title NVARCHAR(200) NOT NULL,
                        GameTitle NVARCHAR(200) NOT NULL,
                        CoreChampion NVARCHAR(200) NOT NULL,
                        CurrentStage NVARCHAR(200) NULL,
                        Augment NVARCHAR(500) NULL,
                        AvailableItems NVARCHAR(500) NULL,
                        Notes NVARCHAR(1000) NULL,
                        Summary NVARCHAR(500) NOT NULL,
                        RecommendedItems NVARCHAR(500) NOT NULL,
                        RecommendedAugments NVARCHAR(700) NOT NULL,
                        InputJson NVARCHAR(MAX) NOT NULL,
                        RecommendationJson NVARCHAR(MAX) NOT NULL,
                        AdoptedCount INT NOT NULL CONSTRAINT DF_AiRecommendationFavorites_AdoptedCount DEFAULT 0,
                        LastAdoptedAt DATETIME2 NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_AiRecommendationFavorites_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_AiRecommendationFavorites_UpdatedAt DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_AiRecommendationFavorites_Username_InputHash UNIQUE (Username, InputHash)
                    );

                    CREATE INDEX IX_AiRecommendationFavorites_Username_UpdatedAt
                        ON dbo.AiRecommendationFavorites (Username, UpdatedAt DESC);

                    CREATE INDEX IX_AiRecommendationFavorites_Username_Champion
                        ON dbo.AiRecommendationFavorites (Username, CoreChampion);
                END";

            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            const string migrateSql = @"
                IF COL_LENGTH(N'dbo.AiRecommendationFavorites', N'AdoptedCount') IS NULL
                BEGIN
                    ALTER TABLE dbo.AiRecommendationFavorites
                    ADD AdoptedCount INT NOT NULL CONSTRAINT DF_AiRecommendationFavorites_AdoptedCount DEFAULT 0;
                END

                IF COL_LENGTH(N'dbo.AiRecommendationFavorites', N'LastAdoptedAt') IS NULL
                BEGIN
                    ALTER TABLE dbo.AiRecommendationFavorites
                    ADD LastAdoptedAt DATETIME2 NULL;
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AiRecommendationFavorites_Adopted'
                      AND object_id = OBJECT_ID(N'dbo.AiRecommendationFavorites')
                )
                BEGIN
                    CREATE INDEX IX_AiRecommendationFavorites_Adopted
                        ON dbo.AiRecommendationFavorites (AdoptedCount DESC, LastAdoptedAt DESC)
                        INCLUDE (CoreChampion, UpdatedAt);
                END";

            await using var migrateCommand = new SqlCommand(migrateSql, connection);
            await migrateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddSaveParameters(
            SqlCommand command,
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
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 256).Value = TrimTo(username, 256);
            command.Parameters.Add("@InputHash", SqlDbType.NVarChar, 64).Value = inputHash;
            command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = title;
            command.Parameters.Add("@GameTitle", SqlDbType.NVarChar, 200).Value = TrimTo(input.GameTitle, 200);
            command.Parameters.Add("@CoreChampion", SqlDbType.NVarChar, 200).Value = TrimTo(input.CoreChampion, 200);
            command.Parameters.Add("@CurrentStage", SqlDbType.NVarChar, 200).Value = DbValueOrNull(input.CurrentStage, 200);
            command.Parameters.Add("@Augment", SqlDbType.NVarChar, 500).Value = DbValueOrNull(input.Augment, 500);
            command.Parameters.Add("@AvailableItems", SqlDbType.NVarChar, 500).Value = DbValueOrNull(input.AvailableItems, 500);
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(input.Notes, 1000);
            command.Parameters.Add("@Summary", SqlDbType.NVarChar, 500).Value = summary;
            command.Parameters.Add("@RecommendedItems", SqlDbType.NVarChar, 500).Value = itemNames;
            command.Parameters.Add("@RecommendedAugments", SqlDbType.NVarChar, 700).Value = augmentNames;
            command.Parameters.Add("@InputJson", SqlDbType.NVarChar, -1).Value = inputJson;
            command.Parameters.Add("@RecommendationJson", SqlDbType.NVarChar, -1).Value = recommendationJson;
        }

        private static AiRecommendationFavorite ReadFavorite(SqlDataReader reader)
        {
            return new AiRecommendationFavorite
            {
                Id = Convert.ToInt32(reader["Id"]),
                Username = ReadString(reader, "Username"),
                Title = ReadString(reader, "Title"),
                GameTitle = ReadString(reader, "GameTitle"),
                CoreChampion = ReadString(reader, "CoreChampion"),
                CurrentStage = ReadNullableString(reader, "CurrentStage"),
                Augment = ReadNullableString(reader, "Augment"),
                AvailableItems = ReadNullableString(reader, "AvailableItems"),
                Notes = ReadNullableString(reader, "Notes"),
                Summary = ReadString(reader, "Summary"),
                RecommendedItems = ReadString(reader, "RecommendedItems"),
                RecommendedAugments = ReadString(reader, "RecommendedAugments"),
                InputJson = ReadString(reader, "InputJson"),
                RecommendationJson = ReadString(reader, "RecommendationJson"),
                AdoptedCount = ReadInt32(reader, "AdoptedCount"),
                LastAdoptedAt = ReadNullableDateTime(reader, "LastAdoptedAt"),
                CreatedAt = ReadDateTime(reader, "CreatedAt"),
                UpdatedAt = ReadDateTime(reader, "UpdatedAt")
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
            return value.Split(new[] { ' ', ',', '，', '、', ';', '；', '/', '／', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            var stage = string.IsNullOrWhiteSpace(input.CurrentStage) ? string.Empty : $"｜{input.CurrentStage}";
            return TrimTo($"{champion}{stage} 推薦", 200);
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

        private static void EnsurePayloadSize(string payload, string label)
        {
            if (payload.Length > MaxSerializedPayloadLength)
            {
                throw new InvalidOperationException($"{label}內容過大，為了保護資料庫與伺服器效能，請縮短後再儲存。");
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

        private static object DbValueOrNull(string? value, int maxLength)
        {
            return TrimTo(value, maxLength) is { Length: > 0 } trimmed ? trimmed : DBNull.Value;
        }

        private static string ReadString(SqlDataReader reader, string name)
        {
            return reader[name] == DBNull.Value ? string.Empty : reader[name].ToString() ?? string.Empty;
        }

        private static string? ReadNullableString(SqlDataReader reader, string name)
        {
            return reader[name] == DBNull.Value ? null : reader[name].ToString();
        }

        private static DateTime ReadDateTime(SqlDataReader reader, string name)
        {
            return reader[name] == DBNull.Value ? DateTime.UtcNow : Convert.ToDateTime(reader[name]);
        }

        private static DateTime? ReadNullableDateTime(SqlDataReader reader, string name)
        {
            return reader[name] == DBNull.Value ? null : Convert.ToDateTime(reader[name]);
        }

        private static int ReadInt32(SqlDataReader reader, string name)
        {
            return reader[name] == DBNull.Value ? 0 : Convert.ToInt32(reader[name]);
        }
    }
}
