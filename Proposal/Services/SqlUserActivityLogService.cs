using System.Data;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services
{
    public class SqlUserActivityLogService : IUserActivityLogService
    {
        private const int MaxLogsPerUser = 5;
        private const int MaxUsernameLength = 100;
        private const int MaxCategoryLength = 40;
        private const int MaxTitleLength = 120;
        private const int MaxDetailLength = 500;
        private const int MaxLinkUrlLength = 300;

        private readonly ISqlConnectionFactory _connectionFactory;

        public SqlUserActivityLogService(ISqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task AddAsync(
            string username,
            string category,
            string title,
            string detail,
            string? linkUrl = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureTableAsync(connection, cancellationToken);

            const string insertSql = @"
                INSERT INTO UserActivityLogs (Username, Category, Title, Detail, LinkUrl)
                VALUES (@Username, @Category, @Title, @Detail, @LinkUrl);

                WITH RankedLogs AS
                (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY Username ORDER BY CreatedAt DESC, Id DESC) AS RowNumber
                    FROM UserActivityLogs
                    WHERE Username = @Username
                )
                DELETE FROM UserActivityLogs
                WHERE Id IN (SELECT Id FROM RankedLogs WHERE RowNumber > @MaxLogs);";

            await using var command = new SqlCommand(insertSql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, MaxUsernameLength).Value = TrimTo(username, MaxUsernameLength);
            command.Parameters.Add("@Category", SqlDbType.NVarChar, MaxCategoryLength).Value = TrimTo(category, MaxCategoryLength);
            command.Parameters.Add("@Title", SqlDbType.NVarChar, MaxTitleLength).Value = TrimTo(title, MaxTitleLength);
            command.Parameters.Add("@Detail", SqlDbType.NVarChar, MaxDetailLength).Value = TrimTo(detail, MaxDetailLength);
            command.Parameters.Add("@LinkUrl", SqlDbType.NVarChar, MaxLinkUrlLength).Value = DbUrlOrNull(linkUrl);
            command.Parameters.Add("@MaxLogs", SqlDbType.Int).Value = MaxLogsPerUser;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<UserActivityLog>> GetRecentAsync(
            string username,
            int limit = 5,
            CancellationToken cancellationToken = default)
        {
            var logs = new List<UserActivityLog>();
            if (string.IsNullOrWhiteSpace(username))
            {
                return logs;
            }

            await using var connection = _connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);
            await EnsureTableAsync(connection, cancellationToken);

            const string sql = @"
                SELECT TOP (@Limit)
                       Id, Username, Category, Title, Detail, LinkUrl, CreatedAt
                FROM UserActivityLogs
                WHERE Username = @Username
                ORDER BY CreatedAt DESC, Id DESC;";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, MaxUsernameLength).Value = TrimTo(username, MaxUsernameLength);
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(limit, 1, MaxLogsPerUser);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                logs.Add(new UserActivityLog
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Username = ReadString(reader, "Username"),
                    Category = ReadString(reader, "Category"),
                    Title = ReadString(reader, "Title"),
                    Detail = ReadString(reader, "Detail"),
                    LinkUrl = reader.IsDBNull(reader.GetOrdinal("LinkUrl")) ? null : ReadString(reader, "LinkUrl"),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                });
            }

            return logs;
        }

        private static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string sql = @"
                IF OBJECT_ID(N'dbo.UserActivityLogs', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.UserActivityLogs
                    (
                        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserActivityLogs PRIMARY KEY,
                        Username NVARCHAR(100) NOT NULL,
                        Category NVARCHAR(40) NOT NULL,
                        Title NVARCHAR(120) NOT NULL,
                        Detail NVARCHAR(500) NOT NULL,
                        LinkUrl NVARCHAR(300) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_UserActivityLogs_CreatedAt DEFAULT SYSUTCDATETIME()
                    );

                    CREATE INDEX IX_UserActivityLogs_Username_CreatedAt
                        ON dbo.UserActivityLogs (Username, CreatedAt DESC);
                END";

            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static string TrimTo(string? value, int length)
        {
            var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            return text.Length <= length ? text : text[..length];
        }

        private static object DbUrlOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DBNull.Value;
            }

            var trimmed = TrimTo(value, MaxLinkUrlLength);
            return Uri.TryCreate(trimmed, UriKind.Relative, out _)
                && trimmed.StartsWith("/", StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                    ? trimmed
                    : DBNull.Value;
        }

        private static string ReadString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }
    }
}
