using System.Data;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services
{
    public class SqlLolAramGuideRepository : ILolAramGuideRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;

        public SqlLolAramGuideRepository(ISqlConnectionFactory connectionFactory)
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
            await EnsureReadyAsync(connection, cancellationToken);

            var hasSearch = !string.IsNullOrWhiteSpace(searchText);
            var sql = @"
                SELECT TOP 200
                    Id,
                    ChampionKey,
                    ChampionName,
                    LocalizedName,
                    ModeName,
                    PatchVersion,
                    RoleSummary,
                    CoreItems,
                    SituationalItems,
                    Augments,
                    SummonerSpells,
                    SkillOrder,
                    PlaystyleTips,
                    PositioningTips,
                    Weaknesses,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramGuides";

            if (hasSearch)
            {
                sql += @"
                WHERE ChampionKey LIKE @Search
                   OR ChampionName LIKE @Search
                   OR LocalizedName LIKE @Search
                   OR ModeName LIKE @Search";
            }

            sql += @"
                ORDER BY ChampionKey, ModeName";

            await using var command = new SqlCommand(sql, connection);
            if (hasSearch)
            {
                command.Parameters.Add("@Search", SqlDbType.NVarChar, 120).Value = $"%{TrimTo(searchText, 118)}%";
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
            await EnsureReadyAsync(connection, cancellationToken);

            const string sql = @"
                SELECT
                    Id,
                    ChampionKey,
                    ChampionName,
                    LocalizedName,
                    ModeName,
                    PatchVersion,
                    RoleSummary,
                    CoreItems,
                    SituationalItems,
                    Augments,
                    SummonerSpells,
                    SkillOrder,
                    PlaystyleTips,
                    PositioningTips,
                    Weaknesses,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramGuides
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadGuide(reader);
        }

        public async Task CreateAsync(LolAramGuide guide, CancellationToken cancellationToken = default)
        {
            PrepareGuide(guide);

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureReadyAsync(connection, cancellationToken);

            const string sql = @"
                INSERT INTO LolAramGuides
                    (ChampionKey, ChampionName, LocalizedName, ModeName, PatchVersion, RoleSummary,
                     CoreItems, SituationalItems, Augments, SummonerSpells, SkillOrder,
                     PlaystyleTips, PositioningTips, Weaknesses, SourceUrl, Notes)
                VALUES
                    (@ChampionKey, @ChampionName, @LocalizedName, @ModeName, @PatchVersion, @RoleSummary,
                     @CoreItems, @SituationalItems, @Augments, @SummonerSpells, @SkillOrder,
                     @PlaystyleTips, @PositioningTips, @Weaknesses, @SourceUrl, @Notes)";

            await using var command = new SqlCommand(sql, connection);
            AddGuideParameters(command, guide);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> UpdateAsync(LolAramGuide guide, CancellationToken cancellationToken = default)
        {
            PrepareGuide(guide);

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureReadyAsync(connection, cancellationToken);

            const string sql = @"
                UPDATE LolAramGuides
                SET ChampionKey = @ChampionKey,
                    ChampionName = @ChampionName,
                    LocalizedName = @LocalizedName,
                    ModeName = @ModeName,
                    PatchVersion = @PatchVersion,
                    RoleSummary = @RoleSummary,
                    CoreItems = @CoreItems,
                    SituationalItems = @SituationalItems,
                    Augments = @Augments,
                    SummonerSpells = @SummonerSpells,
                    SkillOrder = @SkillOrder,
                    PlaystyleTips = @PlaystyleTips,
                    PositioningTips = @PositioningTips,
                    Weaknesses = @Weaknesses,
                    SourceUrl = @SourceUrl,
                    Notes = @Notes,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = guide.Id;
            AddGuideParameters(command, guide);
            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0;
        }

        public async Task<GuideImportResult> UpsertManyAsync(
            IEnumerable<LolAramGuide> guides,
            CancellationToken cancellationToken = default)
        {
            var result = new GuideImportResult();

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureReadyAsync(connection, cancellationToken);

            foreach (var guide in guides)
            {
                PrepareGuide(guide);
                if (string.IsNullOrWhiteSpace(guide.ChampionKey)
                    || string.IsNullOrWhiteSpace(guide.ChampionName)
                    || string.IsNullOrWhiteSpace(guide.RoleSummary)
                    || string.IsNullOrWhiteSpace(guide.CoreItems))
                {
                    result.SkippedCount++;
                    continue;
                }

                var existingId = await FindExistingIdAsync(connection, guide, cancellationToken);
                if (existingId.HasValue)
                {
                    await UpdateByIdAsync(connection, existingId.Value, guide, cancellationToken);
                    result.UpdatedCount++;
                }
                else
                {
                    await InsertAsync(connection, guide, cancellationToken);
                    result.InsertedCount++;
                }
            }

            return result;
        }

        public async Task<GuideImportResult> UpsertAugmentRecommendationsAsync(
            IEnumerable<LolAramGuide> guides,
            CancellationToken cancellationToken = default)
        {
            var result = new GuideImportResult();

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureReadyAsync(connection, cancellationToken);

            foreach (var guide in guides)
            {
                PrepareGuide(guide);
                if (string.IsNullOrWhiteSpace(guide.ChampionKey)
                    || string.IsNullOrWhiteSpace(guide.ChampionName)
                    || string.IsNullOrWhiteSpace(guide.Augments))
                {
                    result.SkippedCount++;
                    continue;
                }

                var existingId = await FindExistingIdAsync(connection, guide, cancellationToken);
                if (existingId.HasValue)
                {
                    await UpdateAugmentsByIdAsync(connection, existingId.Value, guide, cancellationToken);
                    result.UpdatedCount++;
                }
                else
                {
                    await InsertAsync(connection, guide, cancellationToken);
                    result.InsertedCount++;
                }
            }

            return result;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureReadyAsync(connection, cancellationToken);

            const string sql = "DELETE FROM LolAramGuides WHERE Id = @Id";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0;
        }

        private static async Task EnsureReadyAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await LolAramGuideSchema.EnsureTableAsync(connection, cancellationToken);
            await LolAramGuideSchema.SeedBrandGuideAsync(connection, cancellationToken);
        }

        private static async Task<int?> FindExistingIdAsync(
            SqlConnection connection,
            LolAramGuide guide,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 1 Id
                FROM LolAramGuides
                WHERE ChampionKey = @ChampionKey AND ModeName = @ModeName";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@ChampionKey", SqlDbType.NVarChar, 100).Value = TrimTo(guide.ChampionKey, 100);
            command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = TrimTo(guide.ModeName, 100);
            var id = await command.ExecuteScalarAsync(cancellationToken);
            return id == null || id == DBNull.Value ? null : (int)id;
        }

        private static async Task InsertAsync(
            SqlConnection connection,
            LolAramGuide guide,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO LolAramGuides
                    (ChampionKey, ChampionName, LocalizedName, ModeName, PatchVersion, RoleSummary,
                     CoreItems, SituationalItems, Augments, SummonerSpells, SkillOrder,
                     PlaystyleTips, PositioningTips, Weaknesses, SourceUrl, Notes)
                VALUES
                    (@ChampionKey, @ChampionName, @LocalizedName, @ModeName, @PatchVersion, @RoleSummary,
                     @CoreItems, @SituationalItems, @Augments, @SummonerSpells, @SkillOrder,
                     @PlaystyleTips, @PositioningTips, @Weaknesses, @SourceUrl, @Notes)";

            await using var command = new SqlCommand(sql, connection);
            AddGuideParameters(command, guide);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task UpdateByIdAsync(
            SqlConnection connection,
            int id,
            LolAramGuide guide,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE LolAramGuides
                SET ChampionKey = @ChampionKey,
                    ChampionName = @ChampionName,
                    LocalizedName = @LocalizedName,
                    ModeName = @ModeName,
                    PatchVersion = @PatchVersion,
                    RoleSummary = @RoleSummary,
                    CoreItems = @CoreItems,
                    SituationalItems = @SituationalItems,
                    Augments = @Augments,
                    SummonerSpells = @SummonerSpells,
                    SkillOrder = @SkillOrder,
                    PlaystyleTips = @PlaystyleTips,
                    PositioningTips = @PositioningTips,
                    Weaknesses = @Weaknesses,
                    SourceUrl = @SourceUrl,
                    Notes = @Notes,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            AddGuideParameters(command, guide);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task UpdateAugmentsByIdAsync(
            SqlConnection connection,
            int id,
            LolAramGuide guide,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE LolAramGuides
                SET ChampionName = @ChampionName,
                    LocalizedName = @LocalizedName,
                    PatchVersion = @PatchVersion,
                    Augments = @Augments,
                    SourceUrl = @SourceUrl,
                    Notes = @Notes,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            command.Parameters.Add("@ChampionName", SqlDbType.NVarChar, 100).Value = TrimTo(guide.ChampionName, 100);
            command.Parameters.Add("@LocalizedName", SqlDbType.NVarChar, 100).Value = DbValueOrNull(guide.LocalizedName, 100);
            command.Parameters.Add("@PatchVersion", SqlDbType.NVarChar, 50).Value = TrimTo(guide.PatchVersion, 50);
            command.Parameters.Add("@Augments", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(guide.Augments, 1000);
            command.Parameters.Add("@SourceUrl", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(guide.SourceUrl, 1000);
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(guide.Notes, 1200);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddGuideParameters(SqlCommand command, LolAramGuide guide)
        {
            command.Parameters.Add("@ChampionKey", SqlDbType.NVarChar, 100).Value = TrimTo(guide.ChampionKey, 100);
            command.Parameters.Add("@ChampionName", SqlDbType.NVarChar, 100).Value = TrimTo(guide.ChampionName, 100);
            command.Parameters.Add("@LocalizedName", SqlDbType.NVarChar, 100).Value = DbValueOrNull(guide.LocalizedName, 100);
            command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = TrimTo(guide.ModeName, 100);
            command.Parameters.Add("@PatchVersion", SqlDbType.NVarChar, 50).Value = TrimTo(guide.PatchVersion, 50);
            command.Parameters.Add("@RoleSummary", SqlDbType.NVarChar, 500).Value = TrimTo(guide.RoleSummary, 500);
            command.Parameters.Add("@CoreItems", SqlDbType.NVarChar, 1000).Value = TrimTo(guide.CoreItems, 1000);
            command.Parameters.Add("@SituationalItems", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(guide.SituationalItems, 1000);
            command.Parameters.Add("@Augments", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(guide.Augments, 1000);
            command.Parameters.Add("@SummonerSpells", SqlDbType.NVarChar, 500).Value = DbValueOrNull(guide.SummonerSpells, 500);
            command.Parameters.Add("@SkillOrder", SqlDbType.NVarChar, 500).Value = DbValueOrNull(guide.SkillOrder, 500);
            command.Parameters.Add("@PlaystyleTips", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(guide.PlaystyleTips, 1200);
            command.Parameters.Add("@PositioningTips", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(guide.PositioningTips, 1200);
            command.Parameters.Add("@Weaknesses", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(guide.Weaknesses, 1200);
            command.Parameters.Add("@SourceUrl", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(guide.SourceUrl, 1000);
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(guide.Notes, 1200);
        }

        private static void PrepareGuide(LolAramGuide guide)
        {
            guide.ChampionKey = NormalizeChampionKey(guide.ChampionKey);
            guide.ModeName = string.IsNullOrWhiteSpace(guide.ModeName) ? "ARAM Mayhem" : guide.ModeName.Trim();
            guide.PatchVersion = string.IsNullOrWhiteSpace(guide.PatchVersion) ? "manual" : guide.PatchVersion.Trim();
        }

        private static string NormalizeChampionKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        }

        private static LolAramGuide ReadGuide(SqlDataReader reader)
        {
            return new LolAramGuide
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                ChampionKey = ReadString(reader, "ChampionKey"),
                ChampionName = ReadString(reader, "ChampionName"),
                LocalizedName = ReadNullableString(reader, "LocalizedName"),
                ModeName = ReadString(reader, "ModeName"),
                PatchVersion = ReadString(reader, "PatchVersion"),
                RoleSummary = ReadString(reader, "RoleSummary"),
                CoreItems = ReadString(reader, "CoreItems"),
                SituationalItems = ReadNullableString(reader, "SituationalItems"),
                Augments = ReadNullableString(reader, "Augments"),
                SummonerSpells = ReadNullableString(reader, "SummonerSpells"),
                SkillOrder = ReadNullableString(reader, "SkillOrder"),
                PlaystyleTips = ReadNullableString(reader, "PlaystyleTips"),
                PositioningTips = ReadNullableString(reader, "PositioningTips"),
                Weaknesses = ReadNullableString(reader, "Weaknesses"),
                SourceUrl = ReadNullableString(reader, "SourceUrl"),
                Notes = ReadNullableString(reader, "Notes"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }

        private static string ReadString(SqlDataReader reader, string columnName)
        {
            return reader.GetString(reader.GetOrdinal(columnName));
        }

        private static string? ReadNullableString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
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
    }
}
