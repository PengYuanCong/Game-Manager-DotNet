namespace Proposal.Services;

public interface IPasswordHashService
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string? storedPassword, out bool needsRehash);
}
