using System.ComponentModel;

namespace MusicalEncryptionServer.Models;

public record RegisterRequest(
    [property: Description("3-20 characters.")] string Username,
    [property: Description("At least 8 characters.")] string Password);

public record LoginRequest(string Username, string Password);

public record ChangePasswordRequest(string Username, string OldPassword, string NewPassword);

/// <summary>Body for the dev control panel's password-override action. No old password needed — it's a dev admin action.</summary>
public record DevSetPasswordRequest(string NewPassword);

public record UserSummary(
    string Id,
    [property: Description("Use this value as the X-User-Id header on /api/encryption calls.")] string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsBanned);
