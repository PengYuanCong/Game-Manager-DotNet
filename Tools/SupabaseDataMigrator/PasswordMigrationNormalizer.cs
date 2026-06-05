using System.Security.Cryptography;

internal static class PasswordMigrationNormalizer
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string Normalize(string? storedPassword, out bool upgraded)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            throw new InvalidOperationException("A user password is empty and cannot be migrated.");
        }

        if (storedPassword.StartsWith($"{Prefix}$", StringComparison.Ordinal))
        {
            ValidateCurrentHash(storedPassword);
            upgraded = false;
            return storedPassword;
        }

        upgraded = true;
        return Hash(storedPassword);
    }

    public static PasswordMigrationKind Classify(string? storedPassword)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return PasswordMigrationKind.Invalid;
        }

        if (!storedPassword.StartsWith($"{Prefix}$", StringComparison.Ordinal))
        {
            return PasswordMigrationKind.Legacy;
        }

        try
        {
            ValidateCurrentHash(storedPassword);
            return PasswordMigrationKind.Current;
        }
        catch (InvalidOperationException)
        {
            return PasswordMigrationKind.Invalid;
        }
    }

    internal static bool Verify(string password, string encodedHash)
    {
        ValidateCurrentHash(encodedHash);
        var parts = encodedHash.Split('$');
        var iterations = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return string.Join(
            '$',
            Prefix,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    private static void ValidateCurrentHash(string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 4
            || parts[0] != Prefix
            || !int.TryParse(parts[1], out var iterations)
            || iterations <= 0)
        {
            throw new InvalidOperationException("A stored PBKDF2 password hash is malformed.");
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);
            if (salt.Length == 0 || hash.Length == 0)
            {
                throw new InvalidOperationException("A stored PBKDF2 password hash is malformed.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("A stored PBKDF2 password hash is malformed.", exception);
        }
    }
}

internal enum PasswordMigrationKind
{
    Current,
    Legacy,
    Invalid
}
