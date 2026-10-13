using System.Collections.Concurrent;
using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

/// <summary>
/// Keeps registered users in memory. Same story as
/// <see cref="InMemoryEncryptionRepository"/>: good enough to build and test
/// with now, swap for <c>FirestoreUserRepository</c> later without touching
/// the controller.
/// </summary>
public class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, User> _byId = new();
    private readonly ConcurrentDictionary<string, string> _idByUsername = new(StringComparer.OrdinalIgnoreCase);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        if (_idByUsername.TryGetValue(username, out var id) && _byId.TryGetValue(id, out var user))
            return Task.FromResult<User?>(user);
        return Task.FromResult<User?>(null);
    }

    public Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_byId.TryGetValue(id, out var user) ? user : null);

    public Task<bool> AddAsync(User user, CancellationToken ct = default)
    {
        if (!_idByUsername.TryAdd(user.Username, user.Id))
            return Task.FromResult(false); // username already taken

        _byId[user.Id] = user;
        return Task.FromResult(true);
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        // Mutations already apply in place (we hand out the same object reference), but we
        // still write it back so behavior matches FirestoreUserRepository's contract.
        _byId[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<User> all = _byId.Values.OrderBy(u => u.CreatedAt).ToList();
        return Task.FromResult(all);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!_byId.TryRemove(id, out var user))
            return Task.FromResult(false);

        _idByUsername.TryRemove(user.Username, out _);
        return Task.FromResult(true);
    }
}
