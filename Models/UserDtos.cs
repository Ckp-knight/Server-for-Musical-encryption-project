using System.ComponentModel;

namespace MusicalEncryptionServer.Models;

public record RegisterRequest(
    [property: Description("3-20 characters.")] string Username,
    [property: Description("At least 8 characters.")] string Password);

public record LoginRequest(string Username, string Password);

public record UserSummary(
    string Id,
    [property: Description("Use this value as the X-User-Id header on /api/encryption calls.")] string Username,
    DateTimeOffset CreatedAt);
