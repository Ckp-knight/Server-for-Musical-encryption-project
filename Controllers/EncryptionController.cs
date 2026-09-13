using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Repositories;
using MusicalEncryptionServer.Services;

namespace MusicalEncryptionServer.Controllers;

/// <summary>
/// The server's job: take requests from the user, run the encryption, enforce
/// permissions (a user only sees their own records), and store/read through the
/// repository. The user never touches the database directly.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EncryptionController : ControllerBase
{
    private readonly IEncryptionRepository _repo;
    private readonly IMusicalCipher _cipher;
    private readonly ILogger<EncryptionController> _logger;

    public EncryptionController(
        IEncryptionRepository repo,
        IMusicalCipher cipher,
        ILogger<EncryptionController> logger)
    {
        _repo = repo;
        _cipher = cipher;
        _logger = logger;
    }

    private const string UserIdHeaderDescription =
        "TEMPORARY stand-in for real login. Type any name (e.g. \"user-123\") — the server " +
        "treats it as \"who is making this request\" and only lets you see/decrypt/delete " +
        "records created under that same name. Replace with real authentication later.";

    [HttpPost]
    [EndpointSummary("Encrypt some text and save it")]
    [EndpointDescription(
        "Body: Title (label you choose), PlainText (the secret text), Key (used to encrypt it — " +
        "you'll need the exact same Key later to decrypt). Returns the new record's Id — copy it, " +
        "you need it for the other endpoints below.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EncryptResponse>> Encrypt(
        [FromHeader(Name = "X-User-Id"), Description(UserIdHeaderDescription)] string? userId,
        [FromBody] EncryptRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized("Missing X-User-Id header.");
        if (string.IsNullOrWhiteSpace(request.PlainText))
            return BadRequest("PlainText is required.");
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest("Key is required.");

        var record = new EncryptionRecord
        {
            OwnerUserId = userId,
            Title = request.Title ?? string.Empty,
            CipherText = _cipher.Encrypt(request.PlainText, request.Key),
            Algorithm = _cipher.AlgorithmName,
        };

        await _repo.AddAsync(record, ct);
        _logger.LogInformation("User {UserId} created record {RecordId}", userId, record.Id);

        return CreatedAtAction(nameof(GetById), new { id = record.Id },
            new EncryptResponse(record.Id, record.CipherText, record.CreatedAt));
    }

    [HttpGet("{id}")]
    [EndpointSummary("Get one record (owner only)")]
    [EndpointDescription(
        "Path parameter Id = the id you got back from POST /api/encryption. Only the X-User-Id " +
        "that created the record can read it — anyone else gets 403 Forbidden.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EncryptionRecord>> GetById(
        string id,
        [FromHeader(Name = "X-User-Id"), Description(UserIdHeaderDescription)] string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized("Missing X-User-Id header.");

        var record = await _repo.GetByIdAsync(id, ct);
        if (record is null)
            return NotFound();
        if (record.OwnerUserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "You do not own this record.");

        return record;
    }

    [HttpGet]
    [EndpointSummary("List my records")]
    [EndpointDescription("Returns only the records created under this X-User-Id — no ids of others' records leak here.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<RecordSummary>>> GetMine(
        [FromHeader(Name = "X-User-Id"), Description(UserIdHeaderDescription)] string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized("Missing X-User-Id header.");

        var records = await _repo.GetByOwnerAsync(userId, ct);
        return records
            .Select(r => new RecordSummary(r.Id, r.Title, r.CreatedAt))
            .ToList();
    }

    [HttpPost("{id}/decrypt")]
    [EndpointSummary("Decrypt a stored record")]
    [EndpointDescription(
        "Path parameter Id = the record's id. Body: Key must be the exact same Key used to encrypt " +
        "it — a wrong Key returns 400, not the actual plaintext scrambled.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DecryptResponse>> Decrypt(
        string id,
        [FromHeader(Name = "X-User-Id"), Description(UserIdHeaderDescription)] string? userId,
        [FromBody] DecryptRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized("Missing X-User-Id header.");
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest("Key is required.");

        var record = await _repo.GetByIdAsync(id, ct);
        if (record is null)
            return NotFound();
        if (record.OwnerUserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "You do not own this record.");

        try
        {
            var plainText = _cipher.Decrypt(record.CipherText, request.Key);
            return new DecryptResponse(plainText);
        }
        catch
        {
            return BadRequest("Could not decrypt with the provided key.");
        }
    }

    [HttpDelete("{id}")]
    [EndpointSummary("Delete a record (owner only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string id,
        [FromHeader(Name = "X-User-Id"), Description(UserIdHeaderDescription)] string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized("Missing X-User-Id header.");

        var record = await _repo.GetByIdAsync(id, ct);
        if (record is null)
            return NotFound();
        if (record.OwnerUserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "You do not own this record.");

        await _repo.DeleteAsync(id, ct);
        return NoContent();
    }
}
