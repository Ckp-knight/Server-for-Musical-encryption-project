using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Repositories;

namespace MusicalEncryptionServer.Services;

/// <summary>
/// Account creation / login / password change, extracted from the old
/// UsersController so both a transport (sockets, or any future one) can call
/// the exact same logic without duplicating it. Results carry an HTTP-style
/// status code purely as a familiar "what kind of failure" signal — nothing
/// here depends on ASP.NET Core.
/// </summary>
public class UserService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;

    public UserService(IUserRepository users, IPasswordHasher hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public async Task<UserServiceResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length is < 3 or > 20)
            return UserServiceResult.Fail(400, "Username must be 3-20 characters.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return UserServiceResult.Fail(400, "Password must be at least 8 characters.");

        var existing = await _users.GetByUsernameAsync(request.Username, ct);
        if (existing is not null)
            return UserServiceResult.Fail(409, "Username is already taken.");

        var (hash, salt) = _hasher.Hash(request.Password);
        var user = new User
        {
            Username = request.Username,
            PasswordHash = hash,
            PasswordSalt = salt,
        };

        var added = await _users.AddAsync(user, ct);
        if (!added)
            return UserServiceResult.Fail(409, "Username is already taken."); // lost a race with another request

        return UserServiceResult.Ok(201, ToSummary(user));
    }

    public async Task<UserServiceResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return UserServiceResult.Fail(400, "Username and password are required.");

        var user = await _users.GetByUsernameAsync(request.Username, ct);

        // Same message whether the username doesn't exist or the password is wrong —
        // telling them apart would let an attacker discover which usernames are registered.
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            return UserServiceResult.Fail(401, "Invalid username or password.");

        if (user.IsBanned)
            return UserServiceResult.Fail(403, "This account has been banned.");

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user, ct);

        return UserServiceResult.Ok(200, ToSummary(user));
    }

    public async Task<UserServiceResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return UserServiceResult.Fail(400, "New password must be at least 8 characters.");

        var user = await _users.GetByUsernameAsync(request.Username, ct);

        // Same message whether the username doesn't exist or the old password is wrong —
        // same reasoning as Login above.
        if (user is null || !_hasher.Verify(request.OldPassword, user.PasswordHash, user.PasswordSalt))
            return UserServiceResult.Fail(401, "Invalid username or old password.");

        var (hash, salt) = _hasher.Hash(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await _users.UpdateAsync(user, ct);

        return UserServiceResult.Ok(200, ToSummary(user));
    }

    private static UserSummary ToSummary(User user) =>
        new(user.Id, user.Username, user.CreatedAt, user.LastLoginAt, user.IsBanned);
}

public record UserServiceResult(bool Success, int StatusCode, string? Error, UserSummary? User)
{
    public static UserServiceResult Ok(int statusCode, UserSummary user) => new(true, statusCode, null, user);
    public static UserServiceResult Fail(int statusCode, string error) => new(false, statusCode, error, null);
}
