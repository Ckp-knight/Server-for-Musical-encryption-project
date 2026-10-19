using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Services;
using Xunit;

namespace MusicalEncryptionServer.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(string signingKey = TestWebApplicationFactory.TestSigningKey) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "musical-encryption-server",
            Audience = "musical-encryption-client",
            SigningKey = signingKey,
            ExpiryMinutes = 30,
            ClockSkewSeconds = 30,
        }));

    [Fact]
    public void IssueToken_PutsImmutableUserIdInSubjectClaim_NotUsername()
    {
        var service = CreateService();
        var user = new User { Id = "user-id-123", Username = "alice" };

        var token = service.IssueToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        Assert.Equal("user-id-123", jwt.Subject);
        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == "name").Value);
    }

    [Fact]
    public void IssueToken_ExpiresInConfiguredWindow()
    {
        var service = CreateService();
        var user = new User { Id = "user-id-123", Username = "alice" };

        var before = DateTimeOffset.UtcNow;
        var token = service.IssueToken(user);

        Assert.InRange(token.ExpiresAtUtc, before.AddMinutes(29), before.AddMinutes(31));
    }

    [Fact]
    public void Constructor_ThrowsIfSigningKeyMissing()
    {
        Assert.Throws<InvalidOperationException>(() => CreateService(signingKey: ""));
    }

    [Fact]
    public void Constructor_ThrowsIfSigningKeyTooShort()
    {
        Assert.Throws<InvalidOperationException>(() => CreateService(signingKey: "too-short"));
    }
}
