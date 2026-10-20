using System.ComponentModel;

namespace MusicalEncryptionServer.Models;

public record RegisterRequest(
    [property: Description("3-20 characters.")] string Username,
    [property: Description("At least 8 characters.")] string Password);

public record LoginRequest(string Username, string Password);

public record ChangePasswordRequest(string Username, string OldPassword, string NewPassword);

public record UserSummary(
    string Id,
    string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsBanned);
