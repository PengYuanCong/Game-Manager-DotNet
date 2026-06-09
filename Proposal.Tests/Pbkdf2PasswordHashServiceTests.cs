using Proposal.Services;

namespace Proposal.Tests;

public sealed class Pbkdf2PasswordHashServiceTests
{
    private readonly Pbkdf2PasswordHashService _service = new();

    [Fact]
    public void HashPassword_UsesRandomSalt()
    {
        var first = _service.HashPassword("Correct-Horse-42");
        var second = _service.HashPassword("Correct-Horse-42");

        Assert.NotEqual(first, second);
        Assert.StartsWith("pbkdf2-sha256$100000$", first);
    }

    [Fact]
    public void VerifyPassword_AcceptsCorrectPasswordWithoutRehash()
    {
        var stored = _service.HashPassword("Correct-Horse-42");

        var verified = _service.VerifyPassword("Correct-Horse-42", stored, out var needsRehash);

        Assert.True(verified);
        Assert.False(needsRehash);
    }

    [Fact]
    public void VerifyPassword_RejectsIncorrectPassword()
    {
        var stored = _service.HashPassword("Correct-Horse-42");

        var verified = _service.VerifyPassword("Wrong-Password", stored, out var needsRehash);

        Assert.False(verified);
        Assert.False(needsRehash);
    }

    [Fact]
    public void VerifyPassword_AcceptsMatchingLegacyValueAndRequestsUpgrade()
    {
        var verified = _service.VerifyPassword("legacy-password", "legacy-password", out var needsRehash);

        Assert.True(verified);
        Assert.True(needsRehash);
    }

    [Fact]
    public void VerifyPassword_RejectsDifferentLegacyValueAndRequestsUpgrade()
    {
        var verified = _service.VerifyPassword("wrong", "legacy-password", out var needsRehash);

        Assert.False(verified);
        Assert.True(needsRehash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifyPassword_RejectsBlankStoredValue(string? stored)
    {
        var verified = _service.VerifyPassword("anything", stored, out var needsRehash);

        Assert.False(verified);
        Assert.False(needsRehash);
    }

    [Theory]
    [InlineData("pbkdf2-sha256$bad$not-base64$still-not-base64")]
    [InlineData("pbkdf2-sha256$100000$not-base64$still-not-base64")]
    [InlineData("pbkdf2-sha256$100000$YWJj")]
    public void VerifyPassword_RejectsMalformedHash(string stored)
    {
        var verified = _service.VerifyPassword("anything", stored, out _);

        Assert.False(verified);
    }
}
