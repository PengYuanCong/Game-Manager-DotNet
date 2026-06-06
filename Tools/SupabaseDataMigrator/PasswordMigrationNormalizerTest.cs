internal static class PasswordMigrationNormalizerTest
{
    public static int Run()
    {
        const string legacyCredential = "legacy-test-password";

        var firstHash = PasswordMigrationNormalizer.Normalize(legacyCredential, out var firstUpgraded);
        var secondHash = PasswordMigrationNormalizer.Normalize(legacyCredential, out var secondUpgraded);

        Assert(firstUpgraded && secondUpgraded, "Legacy passwords must be upgraded.");
        Assert(firstHash != secondHash, "Legacy password hashes must use independent random salts.");
        Assert(PasswordMigrationNormalizer.Verify(legacyCredential, firstHash), "First PBKDF2 hash did not verify.");
        Assert(PasswordMigrationNormalizer.Verify(legacyCredential, secondHash), "Second PBKDF2 hash did not verify.");

        var preservedHash = PasswordMigrationNormalizer.Normalize(firstHash, out var preservedUpgraded);
        Assert(!preservedUpgraded, "Current PBKDF2 hashes must not be upgraded.");
        Assert(preservedHash == firstHash, "Current PBKDF2 hashes must be preserved exactly.");
        Assert(
            PasswordMigrationNormalizer.Classify(firstHash) == PasswordMigrationKind.Current,
            "Current PBKDF2 hashes must be classified as current.");
        Assert(
            PasswordMigrationNormalizer.Classify(legacyCredential) == PasswordMigrationKind.Legacy,
            "Legacy passwords must be classified as legacy.");
        Assert(
            PasswordMigrationNormalizer.Classify("") == PasswordMigrationKind.Invalid,
            "Empty passwords must be classified as invalid.");
        Assert(
            PasswordMigrationNormalizer.Classify("pbkdf2-sha256$broken") == PasswordMigrationKind.Invalid,
            "Malformed PBKDF2 hashes must be classified as invalid.");

        AssertThrows(null);
        AssertThrows("");
        AssertThrows("   ");
        AssertThrows("pbkdf2-sha256$broken");

        var usersSpec = TableSpecs.All.Single(spec => spec.Name == "users");
        var passwordColumn = usersSpec.Columns.Single(column => column.Target == "password_hash");
        Assert(passwordColumn.ValueNormalizer is not null, "Users.Password must use the migration normalizer.");
        var mappedHash = Convert.ToString(passwordColumn.ValueNormalizer!(legacyCredential));
        Assert(
            mappedHash is not null && PasswordMigrationNormalizer.Verify(legacyCredential, mappedHash),
            "Users.Password mapping did not produce a valid PBKDF2 hash.");

        Console.WriteLine("SUPABASE_PASSWORD_MIGRATION_TEST=PASS");
        return 0;
    }

    private static void AssertThrows(string? value)
    {
        try
        {
            PasswordMigrationNormalizer.Normalize(value, out _);
            throw new InvalidOperationException("Expected password normalization to reject an invalid value.");
        }
        catch (InvalidOperationException exception)
            when (exception.Message != "Expected password normalization to reject an invalid value.")
        {
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
