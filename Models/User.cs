namespace MusicalEncryptionServer.Models;

/// <summary>
/// A registered account. The password is never stored in plain text — only a
/// salted hash (see <see cref="Services.IPasswordHasher"/>).
/// </summary>
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
