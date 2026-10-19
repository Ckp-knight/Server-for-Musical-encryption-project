namespace MusicalEncryptionServer.Services;

/// <summary>
/// Bound from the "Jwt" configuration section. <see cref="SigningKey"/> must never come
/// from appsettings*.json — locally it's supplied via `dotnet user-secrets`, and in
/// production via an environment variable (e.g. Cloud Run + Secret Manager), both of
/// which the ASP.NET Core configuration system reads the same way as any other setting.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 30;
    public int ClockSkewSeconds { get; set; } = 30;
}
