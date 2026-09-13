using System.Collections.Concurrent;
using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

/// <summary>
/// Keeps records in memory (a dictionary). Good enough to build and test the
/// whole server now. Data is lost when the server stops.
/// </summary>
public class InMemoryEncryptionRepository : IEncryptionRepository
{
    private readonly ConcurrentDictionary<string, EncryptionRecord> _store = new();

    public Task<EncryptionRecord?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(id, out var record) ? record : null);

    public Task<IReadOnlyList<EncryptionRecord>> GetByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        IReadOnlyList<EncryptionRecord> result = _store.Values
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(EncryptionRecord record, CancellationToken ct = default)
    {
        _store[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.TryRemove(id, out _));
}
