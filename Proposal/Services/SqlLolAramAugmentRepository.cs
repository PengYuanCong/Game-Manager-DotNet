using System.Data;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services
{
    public class SqlLolAramAugmentRepository : ILolAramAugmentRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;

        public SqlLolAramAugmentRepository(ISqlConnectionFactory connectionFactory)
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
            await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);

            var hasSearch = !string.IsNullOrWhiteSpace(searchText);
            var sql = @"
                SELECT TOP 200
                    Id,
                    AugmentKey,
                    Name,
                    ModeName,
                    Rarity,
                    Tier,
                    SeriesKey,
                    EffectText,
                    Tags,
                    SynergyNotes,
                    PatchVersion,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramAugments";

            if (hasSearch)
            {
                sql += @"
                WHERE AugmentKey LIKE @Search
                   OR Name LIKE @Search
                   OR Rarity LIKE @Search
                   OR Tier LIKE @Search
                   OR SeriesKey LIKE @Search
                   OR Tags LIKE @Search";
            }

            sql += @"
                ORDER BY UpdatedAt DESC, Name";

            await using var command = new SqlCommand(sql, connection);
            if (hasSearch)
            {
                command.Parameters.Add("@Search", SqlDbType.NVarChar, 120).Value = $"%{TrimTo(searchText, 118)}%";
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
            await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);

            const string sql = @"
                SELECT
                    Id,
                    AugmentKey,
                    Name,
                    ModeName,
                    Rarity,
                    Tier,
                    SeriesKey,
                    EffectText,
                    Tags,
                    SynergyNotes,
                    PatchVersion,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramAugments
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadAugment(reader);
        }

        public async Task CreateAsync(LolAramAugment augment, CancellationToken cancellationToken = default)
        {
            PrepareAugment(augment);

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);

            const string sql = @"
                INSERT INTO LolAramAugments
                    (AugmentKey, Name, ModeName, Rarity, Tier, SeriesKey, EffectText, Tags,
                     SynergyNotes, PatchVersion, SourceUrl, Notes)
                VALUES
                    (@AugmentKey, @Name, @ModeName, @Rarity, @Tier, @SeriesKey, @EffectText, @Tags,
                     @SynergyNotes, @PatchVersion, @SourceUrl, @Notes)";

            await using var command = new SqlCommand(sql, connection);
            AddAugmentParameters(command, augment);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> UpdateAsync(LolAramAugment augment, CancellationToken cancellationToken = default)
        {
            PrepareAugment(augment);

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);

            const string sql = @"
                UPDATE LolAramAugments
                SET AugmentKey = @AugmentKey,
                    Name = @Name,
                    ModeName = @ModeName,
                    Rarity = @Rarity,
                    Tier = @Tier,
                    SeriesKey = @SeriesKey,
                    EffectText = @EffectText,
                    Tags = @Tags,
                    SynergyNotes = @SynergyNotes,
                    PatchVersion = @PatchVersion,
                    SourceUrl = @SourceUrl,
                    Notes = @Notes,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = augment.Id;
            AddAugmentParameters(command, augment);
            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);

            const string sql = "DELETE FROM LolAramAugments WHERE Id = @Id";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0;
        }

        public async Task<AugmentImportResult> UpsertManyAsync(
            IEnumerable<LolAramAugment> augments,
            CancellationToken cancellationToken = default)
        {
            var result = new AugmentImportResult();

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);

            foreach (var augment in augments)
            {
                PrepareAugment(augment);
                if (string.IsNullOrWhiteSpace(augment.AugmentKey)
                    || string.IsNullOrWhiteSpace(augment.Name)
                    || string.IsNullOrWhiteSpace(augment.EffectText))
                {
                    result.SkippedCount++;
                    continue;
                }

                var existingId = await FindExistingIdAsync(connection, augment, cancellationToken);
                if (existingId.HasValue)
                {
                    await UpdateByIdAsync(connection, existingId.Value, augment, cancellationToken);
                    result.UpdatedCount++;
                }
                else
                {
                    await InsertAsync(connection, augment, cancellationToken);
                    result.InsertedCount++;
                }
            }

            return result;
        }

        private static async Task<int?> FindExistingIdAsync(
            SqlConnection connection,
            LolAramAugment augment,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 1 Id
                FROM LolAramAugments
                WHERE ModeName = @ModeName
                  AND (AugmentKey = @AugmentKey OR Name = @Name)
                ORDER BY CASE WHEN AugmentKey = @AugmentKey THEN 0 ELSE 1 END";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@AugmentKey", SqlDbType.NVarChar, 100).Value = TrimTo(augment.AugmentKey, 100);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = TrimTo(augment.Name, 100);
            command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = TrimTo(augment.ModeName, 100);
            var id = await command.ExecuteScalarAsync(cancellationToken);
            return id == null || id == DBNull.Value ? null : (int)id;
        }

        private static async Task InsertAsync(
            SqlConnection connection,
            LolAramAugment augment,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO LolAramAugments
                    (AugmentKey, Name, ModeName, Rarity, Tier, SeriesKey, EffectText, Tags,
                     SynergyNotes, PatchVersion, SourceUrl, Notes)
                VALUES
                    (@AugmentKey, @Name, @ModeName, @Rarity, @Tier, @SeriesKey, @EffectText, @Tags,
                     @SynergyNotes, @PatchVersion, @SourceUrl, @Notes)";

            await using var command = new SqlCommand(sql, connection);
            AddAugmentParameters(command, augment);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task UpdateByIdAsync(
            SqlConnection connection,
            int id,
            LolAramAugment augment,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE LolAramAugments
                SET AugmentKey = @AugmentKey,
                    Name = @Name,
                    ModeName = @ModeName,
                    Rarity = @Rarity,
                    Tier = @Tier,
                    SeriesKey = @SeriesKey,
                    EffectText = @EffectText,
                    Tags = @Tags,
                    SynergyNotes = @SynergyNotes,
                    PatchVersion = @PatchVersion,
                    SourceUrl = @SourceUrl,
                    Notes = @Notes,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            AddAugmentParameters(command, augment);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddAugmentParameters(SqlCommand command, LolAramAugment augment)
        {
            command.Parameters.Add("@AugmentKey", SqlDbType.NVarChar, 100).Value = TrimTo(augment.AugmentKey, 100);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = TrimTo(augment.Name, 100);
            command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = TrimTo(augment.ModeName, 100);
            command.Parameters.Add("@Rarity", SqlDbType.NVarChar, 50).Value = TrimTo(augment.Rarity, 50);
            command.Parameters.Add("@Tier", SqlDbType.NVarChar, 20).Value = DbValueOrNull(augment.Tier, 20);
            command.Parameters.Add("@SeriesKey", SqlDbType.NVarChar, 100).Value = DbValueOrNull(augment.SeriesKey, 100);
            command.Parameters.Add("@EffectText", SqlDbType.NVarChar, 1200).Value = TrimTo(augment.EffectText, 1200);
            command.Parameters.Add("@Tags", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(augment.Tags, 1000);
            command.Parameters.Add("@SynergyNotes", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(augment.SynergyNotes, 1200);
            command.Parameters.Add("@PatchVersion", SqlDbType.NVarChar, 50).Value = TrimTo(augment.PatchVersion, 50);
            command.Parameters.Add("@SourceUrl", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(augment.SourceUrl, 1000);
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(augment.Notes, 1200);
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

        private static string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace(" ", "_").ToLowerInvariant();
        }

        private static LolAramAugment ReadAugment(SqlDataReader reader)
        {
            var augment = new LolAramAugment
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                AugmentKey = ReadString(reader, "AugmentKey"),
                Name = ReadString(reader, "Name"),
                ModeName = ReadString(reader, "ModeName"),
                Rarity = ReadString(reader, "Rarity"),
                Tier = ReadNullableString(reader, "Tier"),
                SeriesKey = LolAramAugmentTagNormalizer.NormalizeSeriesKey(ReadNullableString(reader, "SeriesKey")),
                EffectText = ReadString(reader, "EffectText"),
                Tags = ReadNullableString(reader, "Tags"),
                SynergyNotes = ReadNullableString(reader, "SynergyNotes"),
                PatchVersion = ReadString(reader, "PatchVersion"),
                SourceUrl = ReadNullableString(reader, "SourceUrl"),
                Notes = ReadNullableString(reader, "Notes"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };

            augment.Tags = LolAramAugmentTagNormalizer.NormalizeTags(augment.Tags, augment.EffectText, augment.SeriesKey);
            return augment;
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
