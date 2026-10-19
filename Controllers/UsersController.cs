using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IJwtTokenService _tokens;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserRepository users, IPasswordHasher hasher, IJwtTokenService tokens, ILogger<UsersController> logger)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _logger = logger;
    }

    [EnableRateLimiting("auth")]
    [HttpPost]
    [EndpointSummary("Register a new account")]
    [EndpointDescription("Body: Username (3-20 chars) and Password (8+ chars). On success, log in separately with POST /api/users/login to get a bearer token for the /api/encryption endpoints.")]
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
            new UserSummary(user.Id, user.Username, user.CreatedAt, user.LastLoginAt, user.IsBanned));
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    [EndpointSummary("Check a username + password")]
    [EndpointDescription(
        "Body: Username and Password of an account created via POST /api/users. On success, send " +
        "the returned Token as the \"Authorization: Bearer <token>\" header on every /api/encryption call.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Username and password are required.");

        var user = await _users.GetByUsernameAsync(request.Username, ct);

        // Same message whether the username doesn't exist or the password is wrong —
        // telling them apart would let an attacker discover which usernames are registered.
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized("Invalid username or password.");

        if (user.IsBanned)
            return StatusCode(StatusCodes.Status403Forbidden, "This account has been banned.");

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user, ct);

        _logger.LogInformation("User {Username} logged in", user.Username);

        var token = _tokens.IssueToken(user);
        var summary = new UserSummary(user.Id, user.Username, user.CreatedAt, user.LastLoginAt, user.IsBanned);
        return new LoginResponse(summary, token.Value, token.ExpiresAtUtc);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("change-password")]
    [EndpointSummary("Change an account's password")]
    [EndpointDescription(
        "Body: Username, OldPassword (must match the current password) and NewPassword (8+ chars). " +
        "On success the new password takes effect immediately — log in again with POST /api/users/login.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserSummary>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest("New password must be at least 8 characters.");

        var user = await _users.GetByUsernameAsync(request.Username, ct);

        // Same message whether the username doesn't exist or the old password is wrong —
        // same reasoning as Login above.
        if (user is null || !_hasher.Verify(request.OldPassword, user.PasswordHash, user.PasswordSalt))
            return Unauthorized("Invalid username or old password.");

        var (hash, salt) = _hasher.Hash(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await _users.UpdateAsync(user, ct);

        _logger.LogInformation("User {Username} changed their password", user.Username);
        return new UserSummary(user.Id, user.Username, user.CreatedAt, user.LastLoginAt, user.IsBanned);
    }

    [Authorize]
    [HttpGet("{id}")]
    [EndpointSummary("Look up your own account by id")]
    [EndpointDescription("Requires a bearer token from /api/users/login. Only the authenticated user may look up their own id — any other id returns 403.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSummary>> GetById(string id, CancellationToken ct)
    {
        var callerId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (callerId != id)
            return StatusCode(StatusCodes.Status403Forbidden, "You may only look up your own account.");

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        return new UserSummary(user.Id, user.Username, user.CreatedAt, user.LastLoginAt, user.IsBanned);
    }
}
