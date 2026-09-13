namespace MusicalEncryptionServer.Diagnostics;

/// <summary>One row in the dev control panel's live traffic feed.</summary>
public record RequestLogEntry(
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    string? UserId,
    int StatusCode,
    long ElapsedMs);
