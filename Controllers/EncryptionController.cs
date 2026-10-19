using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
[Authorize]
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

    /// <summary>The caller's immutable id, from the validated JWT's "sub" claim — [Authorize]
    /// guarantees it's present and signature/expiry-checked before any action method runs.</summary>
    private string CallerId => User.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    [HttpPost]
    [EndpointSummary("Encrypt some text and save it")]
    [EndpointDescription(
        "Requires a bearer token from /api/users/login. Body: Title (label you choose), PlainText " +
        "(the secret text), Key (used to encrypt it — you'll need the exact same Key later to " +
        "decrypt). Returns the new record's Id — copy it, you need it for the other endpoints below.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EncryptResponse>> Encrypt([FromBody] EncryptRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlainText))
            return BadRequest("PlainText is required.");
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest("Key is required.");

        var record = new EncryptionRecord
        {
            // Always the authenticated caller's id — the client cannot choose or override this.
            OwnerUserId = CallerId,
            Title = request.Title ?? string.Empty,
            CipherText = _cipher.Encrypt(request.PlainText, request.Key),
            Algorithm = _cipher.AlgorithmName,
        };

        await _repo.AddAsync(record, ct);
        _logger.LogInformation("User {UserId} created record {RecordId}", CallerId, record.Id);

        return CreatedAtAction(nameof(GetById), new { id = record.Id },
            new EncryptResponse(record.Id, record.CipherText, record.CreatedAt));
    }

    [HttpGet("{id}")]
    [EndpointSummary("Get one record (owner only)")]
    [EndpointDescription(
        "Path parameter Id = the id you got back from POST /api/encryption. Only the authenticated " +
        "caller that created the record can read it — anyone else gets 403 Forbidden.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EncryptionRecord>> GetById(string id, CancellationToken ct)
    {
        var record = await _repo.GetByIdAsync(id, ct);
        if (record is null)
            return NotFound();
        if (record.OwnerUserId != CallerId)
            return StatusCode(StatusCodes.Status403Forbidden, "You do not own this record.");

        return record;
    }

    [HttpGet]
    [EndpointSummary("List my records")]
    [EndpointDescription("Returns only the records created under the authenticated caller — no ids of others' records leak here.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<RecordSummary>>> GetMine(CancellationToken ct)
    {
        var records = await _repo.GetByOwnerAsync(CallerId, ct);
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
    public async Task<ActionResult<DecryptResponse>> Decrypt(string id, [FromBody] DecryptRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest("Key is required.");

        var record = await _repo.GetByIdAsync(id, ct);
        if (record is null)
            return NotFound();
        if (record.OwnerUserId != CallerId)
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
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var record = await _repo.GetByIdAsync(id, ct);
        if (record is null)
            return NotFound();
        if (record.OwnerUserId != CallerId)
            return StatusCode(StatusCodes.Status403Forbidden, "You do not own this record.");

        await _repo.DeleteAsync(id, ct);
        return NoContent();
    }
}
