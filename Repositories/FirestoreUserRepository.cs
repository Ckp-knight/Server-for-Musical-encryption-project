using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

/// <summary>
/// Stores registered users permanently in Google Cloud Firestore.
/// Mirrors the manual-mapping approach used by <see cref="FirestoreEncryptionRepository"/>.
/// </summary>
public class FirestoreUserRepository : IUserRepository
{
    private const string CollectionName = "users";

    private readonly FirestoreDb _db;

    public FirestoreUserRepository(IConfiguration configuration)
    {
        var projectId = configuration["Firestore:ProjectId"]
            ?? throw new InvalidOperationException(
                "Missing config 'Firestore:ProjectId'.");

        _db = FirestoreDb.Create(projectId);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        // Username lookups/uniqueness are case-insensitive (same as InMemoryUserRepository's
        // StringComparer.OrdinalIgnoreCase dictionary), so we query on a lowercased field.
        var query = _db
            .Collection(CollectionName)
            .WhereEqualTo("UsernameLower", username.ToLowerInvariant())
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(ct);
        var document = snapshot.Documents.FirstOrDefault();

        return document is null ? null : FromDocument(document);
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var snapshot = await _db
            .Collection(CollectionName)
            .Document(id)
            .GetSnapshotAsync(ct);

        return snapshot.Exists ? FromDocument(snapshot) : null;
    }

    public async Task<bool> AddAsync(User user, CancellationToken ct = default)
    {
        var usernameLower = user.Username.ToLowerInvariant();

        // Transaction so the "is this username taken" check and the write are atomic,
        // preserving the same race-free uniqueness guarantee the in-memory dictionary gave us.
        return await _db.RunTransactionAsync(async transaction =>
        {
            var existingQuery = _db
                .Collection(CollectionName)
                .WhereEqualTo("UsernameLower", usernameLower)
                .Limit(1);

            var existing = await transaction.GetSnapshotAsync(existingQuery);
            if (existing.Count > 0)
                return false; // username already taken

            var docRef = _db.Collection(CollectionName).Document(user.Id);
            transaction.Set(docRef, ToDocumentData(user));
            return true;
        }, cancellationToken: ct);
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        return _db
            .Collection(CollectionName)
            .Document(user.Id)
            .SetAsync(ToDocumentData(user), cancellationToken: ct);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        var snapshot = await _db.Collection(CollectionName).GetSnapshotAsync(ct);

        return snapshot.Documents
            .Select(FromDocument)
            .OrderBy(user => user.CreatedAt)
            .ToList();
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var document = _db.Collection(CollectionName).Document(id);

        var snapshot = await document.GetSnapshotAsync(ct);
        if (!snapshot.Exists)
            return false;

        await document.DeleteAsync(cancellationToken: ct);
        return true;
    }

    private static Dictionary<string, object> ToDocumentData(User user)
    {
        var data = new Dictionary<string, object>
        {
            ["Id"] = user.Id,
            ["Username"] = user.Username,
            ["UsernameLower"] = user.Username.ToLowerInvariant(),
            ["PasswordHash"] = user.PasswordHash,
            ["PasswordSalt"] = user.PasswordSalt,
            ["CreatedAt"] = Timestamp.FromDateTimeOffset(user.CreatedAt),
            ["IsBanned"] = user.IsBanned,
        };

        // Omitted entirely when null, mirroring the in-memory User before a first login.
        if (user.LastLoginAt.HasValue)
            data["LastLoginAt"] = Timestamp.FromDateTimeOffset(user.LastLoginAt.Value);

        return data;
    }

    private static User FromDocument(DocumentSnapshot document)
    {
        return new User
        {
            Id = document.GetValue<string>("Id"),
            Username = document.GetValue<string>("Username"),
            PasswordHash = document.GetValue<string>("PasswordHash"),
            PasswordSalt = document.GetValue<string>("PasswordSalt"),
            CreatedAt = document.GetValue<Timestamp>("CreatedAt").ToDateTimeOffset(),
            LastLoginAt = document.ContainsField("LastLoginAt")
                ? document.GetValue<Timestamp>("LastLoginAt").ToDateTimeOffset()
                : null,
            IsBanned = document.GetValue<bool>("IsBanned"),
        };
    }
}
