namespace MusicalEncryptionServer.Diagnostics;

/// <summary>Keeps the most recent HTTP requests in memory so the dev control panel can show them live.</summary>
public interface IRequestLogStore
{
    void Add(RequestLogEntry entry);

    /// <summary>Newest first.</summary>
    IReadOnlyList<RequestLogEntry> GetRecent();
}
