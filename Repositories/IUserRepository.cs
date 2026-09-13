using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <returns>false if the username is already taken.</returns>
    Task<bool> AddAsync(User user, CancellationToken ct = default);

    /// <summary>Everyone registered. Used only by the dev control panel — not part of the public API.</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Used only by the dev control panel's trash button. <returns>false if no such user.</returns></summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
