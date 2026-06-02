namespace Proposal.Services;

public interface IUserAccountRepository
{
    Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);

    Task CreateUserAsync(string username, string passwordHash, CancellationToken cancellationToken = default);

    Task UpdatePasswordHashAsync(string username, string passwordHash, CancellationToken cancellationToken = default);
}
