using System.ComponentModel;

namespace MusicalEncryptionServer.Models;

// Data the client sends in / gets back. Kept separate from EncryptionRecord
// so we never accidentally expose stored fields (like OwnerUserId).
// The [property: Description] on each field is what Scalar shows as the
// little grey explanation text under that field in the request body editor.

public record EncryptRequest(
    [property: Description("A label for you to recognize this record later. Can be empty.")] string Title,
    [property: Description("The secret text to encrypt.")] string PlainText,
    [property: Description("Used to encrypt the text. You must supply the exact same Key to decrypt it later — nothing else can recover it.")] string Key);

public record EncryptResponse(
    [property: Description("Save this — every other endpoint below needs it.")] string Id,
    [property: Description("The encrypted result, as stored.")] string CipherText,
    DateTimeOffset CreatedAt);

public record DecryptRequest(
    [property: Description("Must match the Key used when this record was encrypted.")] string Key);

public record DecryptResponse(
    [property: Description("The original text, recovered.")] string PlainText);

public record RecordSummary(string Id, string Title, DateTimeOffset CreatedAt);
