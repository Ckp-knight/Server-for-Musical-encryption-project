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
    string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsBanned);

public record LoginResponse(
    UserSummary User,
    [property: Description("Send this as \"Authorization: Bearer <token>\" on every /api/encryption call.")] string Token,
    DateTimeOffset ExpiresAtUtc);
