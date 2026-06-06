using System.Security.Cryptography;
using System.Text;

namespace Proposal.Services;

public sealed class Pbkdf2PasswordHashService : IPasswordHashService
{
    private const int PasswordHashIterations = 100_000;
    private const int PasswordSaltSize = 16;
    private const int PasswordKeySize = 32;
    private const string PasswordHashPrefix = "pbkdf2-sha256";

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordHashIterations,
            HashAlgorithmName.SHA256,
            PasswordKeySize);

        return string.Join(
            '$',
            PasswordHashPrefix,
            PasswordHashIterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string? storedPassword, out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return false;
        }

        if (!storedPassword.StartsWith($"{PasswordHashPrefix}$", StringComparison.Ordinal))
        {
            needsRehash = true;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(storedPassword),
                Encoding.UTF8.GetBytes(password));
        }

        var parts = storedPassword.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            needsRehash = iterations < PasswordHashIterations;
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
