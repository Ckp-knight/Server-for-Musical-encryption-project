using Google.Cloud.Firestore;
using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

/// <summary>
/// Stores encryption records permanently in Google Cloud Firestore.
/// </summary>
public class FirestoreEncryptionRepository : IEncryptionRepository
{
    private const string CollectionName = "encryptionRecords";

    private readonly FirestoreDb _db;

    public FirestoreEncryptionRepository(IConfiguration configuration)
    {
        var projectId = configuration["Firestore:ProjectId"]
            ?? throw new InvalidOperationException(
                "Missing config 'Firestore:ProjectId'.");

        _db = FirestoreDb.Create(projectId);
    }

    public async Task<EncryptionRecord?> GetByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        var document = _db
            .Collection(CollectionName)
            .Document(id);

        var snapshot = await document.GetSnapshotAsync(ct);

        if (!snapshot.Exists)
            return null;

        return FromDocument(snapshot);
    }

    public async Task<IReadOnlyList<EncryptionRecord>> GetByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default)
    {
        var query = _db
            .Collection(CollectionName)
            .WhereEqualTo("OwnerUserId", ownerUserId);

        var snapshot = await query.GetSnapshotAsync(ct);

        return snapshot.Documents
            .Select(FromDocument)
            .OrderByDescending(record => record.CreatedAt)
            .ToList();
    }

    public async Task AddAsync(
        EncryptionRecord record,
        CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>
        {
            ["Id"] = record.Id,
            ["OwnerUserId"] = record.OwnerUserId,
            ["Title"] = record.Title,
            ["CipherText"] = record.CipherText,
            ["Algorithm"] = record.Algorithm,
            ["CreatedAt"] = Timestamp.FromDateTimeOffset(record.CreatedAt)
        };

        await _db
            .Collection(CollectionName)
            .Document(record.Id)
            .SetAsync(data, cancellationToken: ct);
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken ct = default)
    {
        var document = _db
            .Collection(CollectionName)
            .Document(id);

        var snapshot = await document.GetSnapshotAsync(ct);

        if (!snapshot.Exists)
            return false;

        await document.DeleteAsync(cancellationToken: ct);

        return true;
    }

    private static EncryptionRecord FromDocument(
        DocumentSnapshot document)
    {
        return new EncryptionRecord
        {
            Id = document.GetValue<string>("Id"),
            OwnerUserId = document.GetValue<string>("OwnerUserId"),
            Title = document.GetValue<string>("Title"),
            CipherText = document.GetValue<string>("CipherText"),
            Algorithm = document.GetValue<string>("Algorithm"),
            CreatedAt = document
                .GetValue<Timestamp>("CreatedAt")
                .ToDateTimeOffset()
        };
    }
}