namespace MusicalEncryptionServer.Models;

/// <summary>
/// One stored item. This is the "document" that will live in Firestore later
/// (collection: "encryptionRecords", document id = <see cref="Id"/>).
/// </summary>
public class EncryptionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Id of the user who created the record. Used for permission checks.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>The encrypted ("musical") payload. The plaintext is never stored.</summary>
    public string CipherText { get; set; } = string.Empty;

    /// <summary>Name/version of the algorithm used, so old records stay readable after changes.</summary>
    public string Algorithm { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
