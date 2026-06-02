using Npgsql;
using NpgsqlTypes;

namespace Proposal.Services;

public sealed class PostgresUserAccountRepository : IUserAccountRepository
{
    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresUserAccountRepository(IPostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select password_hash
            from public.app_users
            where lower(username) = lower(@username)
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select exists (
                select 1
                from public.app_users
                where lower(username) = lower(@username)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task CreateUserAsync(string username, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            insert into public.app_users (username, password_hash)
            values (@username, @password_hash);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);
        command.Parameters.Add("@password_hash", NpgsqlDbType.Varchar, 512).Value = passwordHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdatePasswordHashAsync(string username, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            update public.app_users
            set password_hash = @password_hash
            where lower(username) = lower(@username);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);
        command.Parameters.Add("@password_hash", NpgsqlDbType.Varchar, 512).Value = passwordHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddUsername(NpgsqlCommand command, string username)
    {
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 100).Value = username.Trim();
    }
}
