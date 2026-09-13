using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

/// <summary>
/// Everything the server needs from the database, described without naming a
/// specific database. Today it is backed by <see cref="InMemoryEncryptionRepository"/>;
/// later by <see cref="FirestoreEncryptionRepository"/>. The controller never
/// knows which one it is using.
/// </summary>
public interface IEncryptionRepository
{
    Task<EncryptionRecord?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<EncryptionRecord>> GetByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    Task AddAsync(EncryptionRecord record, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
