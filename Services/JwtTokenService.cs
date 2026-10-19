using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        // Same fail-fast shape as FirestoreEncryptionRepository's project-id check: missing
        // or invalid JWT configuration is a misconfiguration, not something to silently work
        // around — and this is the one place guaranteed to run at startup (see Program.cs).
        if (string.IsNullOrWhiteSpace(_options.Issuer) || string.IsNullOrWhiteSpace(_options.Audience))
        {
            throw new InvalidOperationException(
                "Jwt:Issuer and Jwt:Audience must be configured (see appsettings.json \"Jwt\" section).");
        }
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via `dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\"` " +
                "locally, or via the Jwt__SigningKey environment variable (e.g. from Secret Manager) in production.");
        }
        if (Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken IssueToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            // The immutable id — every authorization check in the API trusts only this.
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            // Display-only; never used for ownership/authorization decisions.
            new Claim("name", user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signingCredentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expires);
    }
}
