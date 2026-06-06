using System.Data;
using Microsoft.Data.SqlClient;

namespace Proposal.Services;

public sealed class SqlUserAccountRepository : IUserAccountRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlUserAccountRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureUsersSecuritySchemaAsync(connection, cancellationToken);

        const string sql = "SELECT TOP 1 [Password] FROM dbo.Users WHERE Username = @User";
        await using var command = new SqlCommand(sql, connection);
        AddUsernameParameter(command, username);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureUsersSecuritySchemaAsync(connection, cancellationToken);

        const string sql = "SELECT COUNT(1) FROM dbo.Users WHERE Username = @User";
        await using var command = new SqlCommand(sql, connection);
        AddUsernameParameter(command, username);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task CreateUserAsync(string username, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureUsersSecuritySchemaAsync(connection, cancellationToken);

        const string sql = "INSERT INTO dbo.Users (Username, [Password]) VALUES (@User, @Pass)";
        await using var command = new SqlCommand(sql, connection);
        AddUsernameParameter(command, username);
        command.Parameters.Add("@Pass", SqlDbType.NVarChar, 512).Value = passwordHash;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdatePasswordHashAsync(string username, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureUsersSecuritySchemaAsync(connection, cancellationToken);

        const string sql = "UPDATE dbo.Users SET [Password] = @Pass WHERE Username = @User";
        await using var command = new SqlCommand(sql, connection);
        AddUsernameParameter(command, username);
        command.Parameters.Add("@Pass", SqlDbType.NVarChar, 512).Value = passwordHash;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    private static async Task EnsureUsersSecuritySchemaAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Users', N'Password') IS NOT NULL
               AND EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.Users')
                      AND name = N'Password'
                      AND max_length > 0
                      AND max_length < 512
               )
            BEGIN
                ALTER TABLE dbo.Users ALTER COLUMN [Password] NVARCHAR(512) NULL;
            END
            """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddUsernameParameter(SqlCommand command, string username)
    {
        command.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = username;
    }
}

