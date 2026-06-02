using System.Data;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services;

public sealed class SqlCalculationHistoryRepository : ICalculationHistoryRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlCalculationHistoryRepository(ISqlConnectionFactory connectionFactory)
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
        await EnsureTableAsync(connection, cancellationToken);

        const string sql = """
            SELECT TOP (@Count)
                Id,
                Username,
                FormulaType,
                InputDetails,
                ResultContent,
                CreatedAt
            FROM dbo.CalculationHistory
            WHERE LOWER(LTRIM(RTRIM(Username))) = LOWER(LTRIM(RTRIM(@User)))
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Count", SqlDbType.Int).Value = Math.Clamp(count, 1, 50);
        command.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = username;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            histories.Add(new CalculationHistory
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Username = ReadString(reader, "Username", username),
                FormulaType = ReadString(reader, "FormulaType", "Unknown formula"),
                InputDetails = ReadString(reader, "InputDetails", "No input"),
                ResultContent = ReadString(reader, "ResultContent", "No result"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
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
        await EnsureTableAsync(connection, cancellationToken);

        const string sql = """
            INSERT INTO dbo.CalculationHistory (Username, FormulaType, InputDetails, ResultContent)
            VALUES (@Username, @FormulaType, @InputDetails, @ResultContent);
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = username;
        command.Parameters.Add("@FormulaType", SqlDbType.NVarChar, 100).Value = formulaType;
        command.Parameters.Add("@InputDetails", SqlDbType.NVarChar, 1000).Value = inputDetails;
        command.Parameters.Add("@ResultContent", SqlDbType.NVarChar, 1000).Value = resultContent;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    private static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CalculationHistory', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CalculationHistory
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CalculationHistory PRIMARY KEY,
                    Username NVARCHAR(100) NOT NULL,
                    FormulaType NVARCHAR(100) NOT NULL,
                    InputDetails NVARCHAR(1000) NOT NULL,
                    ResultContent NVARCHAR(1000) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CalculationHistory_CreatedAt DEFAULT SYSUTCDATETIME()
                );
            END
            """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ReadString(SqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }
}

