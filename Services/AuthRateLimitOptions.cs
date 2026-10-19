namespace MusicalEncryptionServer.Services;

/// <summary>Bound from the "AuthRateLimit" configuration section — how many
/// register/login/change-password attempts one client IP gets per window.</summary>
public class AuthRateLimitOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}
