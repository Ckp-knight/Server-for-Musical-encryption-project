using System.Collections.Concurrent;

namespace MusicalEncryptionServer.Diagnostics;

/// <summary>A fixed-size, thread-safe ring buffer — keeps only the last <see cref="Capacity"/> requests.</summary>
public class InMemoryRequestLogStore : IRequestLogStore
{
    private const int Capacity = 200;

    private readonly ConcurrentQueue<RequestLogEntry> _entries = new();

    public void Add(RequestLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<RequestLogEntry> GetRecent()
        => _entries.Reverse().ToList();
}
