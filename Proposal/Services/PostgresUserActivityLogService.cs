using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresUserActivityLogService : IUserActivityLogService
{
    private const int MaxLogsPerUser = 5;
    private const int MaxUsernameLength = 100;
    private const int MaxCategoryLength = 40;
    private const int MaxTitleLength = 120;
    private const int MaxDetailLength = 500;
    private const int MaxLinkUrlLength = 300;

    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresUserActivityLogService(IPostgresConnectionFactory connectionFactory)
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

        const string sql = """
            insert into public.user_activity_logs
                (username, category, title, detail, link_url)
            values
                (@username, @category, @title, @detail, @link_url);

            delete from public.user_activity_logs
            where id in (
                select id
                from (
                    select id,
                           row_number() over (partition by username order by created_at desc, id desc) as row_number
                    from public.user_activity_logs
                    where username = @username
                ) ranked
                where ranked.row_number > @max_logs
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, MaxUsernameLength).Value = TrimTo(username, MaxUsernameLength);
        command.Parameters.Add("@category", NpgsqlDbType.Varchar, MaxCategoryLength).Value = TrimTo(category, MaxCategoryLength);
        command.Parameters.Add("@title", NpgsqlDbType.Varchar, MaxTitleLength).Value = TrimTo(title, MaxTitleLength);
        command.Parameters.Add("@detail", NpgsqlDbType.Varchar, MaxDetailLength).Value = TrimTo(detail, MaxDetailLength);
        command.Parameters.Add("@link_url", NpgsqlDbType.Varchar, MaxLinkUrlLength).Value = DbUrlOrNull(linkUrl);
        command.Parameters.Add("@max_logs", NpgsqlDbType.Integer).Value = MaxLogsPerUser;
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

        const string sql = """
            select id, username, category, title, detail, link_url, created_at
            from public.user_activity_logs
            where username = @username
            order by created_at desc, id desc
            limit @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, MaxUsernameLength).Value = TrimTo(username, MaxUsernameLength);
        command.Parameters.Add("@limit", NpgsqlDbType.Integer).Value = Math.Clamp(limit, 1, MaxLogsPerUser);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new UserActivityLog
            {
                Id = Convert.ToInt32(reader["id"]),
                Username = ReadString(reader, "username"),
                Category = ReadString(reader, "category"),
                Title = ReadString(reader, "title"),
                Detail = ReadString(reader, "detail"),
                LinkUrl = ReadNullableString(reader, "link_url"),
                CreatedAt = ReadDateTime(reader, "created_at")
            });
        }

        return logs;
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

    private static string ReadString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
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
}
