using MusicalEncryptionServer.Models;

namespace MusicalEncryptionServer.Repositories;

/// <summary>
/// Firestore-backed storage. NOT finished yet — it throws until you implement it.
///
/// How to finish it (do this only when the Firebase project exists):
///   1. dotnet add package Google.Cloud.Firestore
///   2. In the Firebase console create a project, then Project settings ->
///      Service accounts -> "Generate new private key". Save the JSON file
///      OUTSIDE the repo (do not commit it).
///   3. Set an environment variable so the SDK finds the key:
///        GOOGLE_APPLICATION_CREDENTIALS = full path to that JSON file
///   4. Put your project id in appsettings.json under "Firestore:ProjectId".
///   5. Implement the four methods below with FirestoreDb (see notes in each).
///   6. In Program.cs change the one registration line from
///        InMemoryEncryptionRepository  ->  FirestoreEncryptionRepository
///      Nothing else in the project changes.
/// </summary>
public class FirestoreEncryptionRepository : IEncryptionRepository
{
    /// <summary>Firestore collection that holds one document per <see cref="EncryptionRecord"/>.</summary>
    private const string CollectionName = "encryptionRecords";

    // private readonly FirestoreDb _db;
    public FirestoreEncryptionRepository(IConfiguration configuration)
    {
        // var projectId = configuration["Firestore:ProjectId"]
        //     ?? throw new InvalidOperationException("Missing config 'Firestore:ProjectId'.");
        // _db = FirestoreDb.Create(projectId);
    }

    public Task<EncryptionRecord?> GetByIdAsync(string id, CancellationToken ct = default)
        // var snapshot = await _db.Collection(CollectionName).Document(id).GetSnapshotAsync(ct);
        // return snapshot.Exists ? snapshot.ConvertTo<EncryptionRecord>() : null;
        => throw new NotImplementedException("FirestoreEncryptionRepository is not implemented yet.");

    public Task<IReadOnlyList<EncryptionRecord>> GetByOwnerAsync(string ownerUserId, CancellationToken ct = default)
        // var query = _db.Collection(CollectionName).WhereEqualTo("OwnerUserId", ownerUserId);
        // var snapshot = await query.GetSnapshotAsync(ct);
        // return snapshot.Documents.Select(d => d.ConvertTo<EncryptionRecord>()).ToList();
        => throw new NotImplementedException("FirestoreEncryptionRepository is not implemented yet.");

    public Task AddAsync(EncryptionRecord record, CancellationToken ct = default)
        // await _db.Collection(CollectionName).Document(record.Id).SetAsync(record, cancellationToken: ct);
        => throw new NotImplementedException("FirestoreEncryptionRepository is not implemented yet.");

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        // await _db.Collection(CollectionName).Document(id).DeleteAsync(cancellationToken: ct);
        // return true;
        => throw new NotImplementedException("FirestoreEncryptionRepository is not implemented yet.");
}
