namespace MusicalEncryptionServer.Services;

/// <summary>
/// Turns a password into something safe to store. We keep only the hash + a
/// random salt, never the password itself — so even someone who reads the
/// database cannot recover it.
/// </summary>
public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);

    bool Verify(string password, string hash, string salt);
}
