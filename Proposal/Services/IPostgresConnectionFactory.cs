using Npgsql;

namespace Proposal.Services;

public interface IPostgresConnectionFactory
{
    NpgsqlConnection Create();
}

public sealed class PostgresConnectionFactory : IPostgresConnectionFactory
{
    private readonly IConfiguration _configuration;

    public PostgresConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public NpgsqlConnection Create()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is not configured.");
        }

        return new NpgsqlConnection(connectionString);
    }
}
