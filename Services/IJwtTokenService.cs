using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Services;

public interface IJwtTokenService
{
    /// <summary>Issues a signed access token for a user who just passed login. The token's
    /// subject is the user's immutable <see cref="User.Id"/> — never the username.</summary>
    AccessToken IssueToken(User user);
}

public record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
