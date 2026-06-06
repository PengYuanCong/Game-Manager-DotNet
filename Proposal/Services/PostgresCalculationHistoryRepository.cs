using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresCalculationHistoryRepository : ICalculationHistoryRepository
{
    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresCalculationHistoryRepository(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CalculationHistory>> GetRecentAsync(
        string username,
        int count,
        CancellationToken cancellationToken = default)
    {
        var histories = new List<CalculationHistory>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select id, username, formula_type, input_details, result_content, created_at
            from public.calculation_history
            where lower(trim(username)) = lower(trim(@username))
            order by created_at desc, id desc
            limit @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 100).Value = username.Trim();
        command.Parameters.Add("@limit", NpgsqlDbType.Integer).Value = Math.Clamp(count, 1, 50);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            histories.Add(new CalculationHistory
            {
                Id = Convert.ToInt32(reader["id"]),
                Username = ReadString(reader, "username", username),
                FormulaType = ReadString(reader, "formula_type", "Unknown formula"),
                InputDetails = ReadString(reader, "input_details", "No input"),
                ResultContent = ReadString(reader, "result_content", "No result"),
                CreatedAt = ReadDateTime(reader, "created_at")
            });
        }

        return histories;
    }

    public async Task AddAsync(
        string username,
        string formulaType,
        string inputDetails,
        string resultContent,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            insert into public.calculation_history
                (username, formula_type, input_details, result_content)
            values
                (@username, @formula_type, @input_details, @result_content);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 100).Value = TrimTo(username, 100);
        command.Parameters.Add("@formula_type", NpgsqlDbType.Varchar, 100).Value = TrimTo(formulaType, 100);
        command.Parameters.Add("@input_details", NpgsqlDbType.Varchar, 1000).Value = TrimTo(inputDetails, 1000);
        command.Parameters.Add("@result_content", NpgsqlDbType.Varchar, 1000).Value = TrimTo(resultContent, 1000);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string TrimTo(string? value, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string ReadString(NpgsqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
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
