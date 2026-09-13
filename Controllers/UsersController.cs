using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Repositories;
using MusicalEncryptionServer.Services;

namespace MusicalEncryptionServer.Controllers;

/// <summary>Account creation and login. This is what makes "Create Account" / "Login" in the WPF client persist and check a user.</summary>
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserRepository users, IPasswordHasher hasher, ILogger<UsersController> logger)
    {
        _users = users;
        _hasher = hasher;
        _logger = logger;
    }

    [HttpPost]
    [EndpointSummary("Register a new account")]
    [EndpointDescription(
        "Body: Username (3-20 chars) and Password (8+ chars). No X-User-Id header needed here — " +
        "there's no user yet. On success, log in separately with POST /api/users/login to get a " +
        "usable X-User-Id for the /api/encryption endpoints.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserSummary>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length is < 3 or > 20)
            return BadRequest("Username must be 3-20 characters.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters.");

        var existing = await _users.GetByUsernameAsync(request.Username, ct);
        if (existing is not null)
            return Conflict("Username is already taken.");

        var (hash, salt) = _hasher.Hash(request.Password);
        var user = new User
        {
            Username = request.Username,
            PasswordHash = hash,
            PasswordSalt = salt,
        };

        var added = await _users.AddAsync(user, ct);
        if (!added)
            return Conflict("Username is already taken."); // lost a race with another request

        _logger.LogInformation("Registered new user {Username} ({UserId})", user.Username, user.Id);

        return CreatedAtAction(nameof(GetById), new { id = user.Id },
            new UserSummary(user.Id, user.Username, user.CreatedAt));
    }

    [HttpPost("login")]
    [EndpointSummary("Check a username + password")]
    [EndpointDescription(
        "Body: Username and Password of an account created via POST /api/users. On success, use " +
        "the returned Username as the X-User-Id header on every /api/encryption call.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserSummary>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Username and password are required.");

        var user = await _users.GetByUsernameAsync(request.Username, ct);

        // Same message whether the username doesn't exist or the password is wrong —
        // telling them apart would let an attacker discover which usernames are registered.
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized("Invalid username or password.");

        _logger.LogInformation("User {Username} logged in", user.Username);
        return new UserSummary(user.Id, user.Username, user.CreatedAt);
    }

    [HttpGet("{id}")]
    [EndpointSummary("Look up an account by id")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSummary>> GetById(string id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        return new UserSummary(user.Id, user.Username, user.CreatedAt);
    }
}
