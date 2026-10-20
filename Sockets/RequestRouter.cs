using System.Text.Json;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Services;

namespace MusicalEncryptionServer.Sockets;

/// <summary>Maps an incoming SocketRequest's Action to the matching UserService call.</summary>
public class RequestRouter
{
    private readonly UserService _users;

    public RequestRouter(UserService users)
    {
        _users = users;
    }

    public async Task<SocketResponse> RouteAsync(SocketRequest request, CancellationToken ct)
    {
        try
        {
            return request.Action switch
            {
                "register" => ToResponse(await _users.RegisterAsync(request.GetPayload<RegisterRequest>(), ct)),
                "login" => ToResponse(await _users.LoginAsync(request.GetPayload<LoginRequest>(), ct)),
                "changePassword" => ToResponse(await _users.ChangePasswordAsync(request.GetPayload<ChangePasswordRequest>(), ct)),
                _ => SocketResponse.Fail(400, $"Unknown action '{request.Action}'."),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return SocketResponse.Fail(400, "Malformed request payload.");
        }
    }

    private static SocketResponse ToResponse(UserServiceResult result) =>
        result.Success ? SocketResponse.Ok(result.StatusCode, result.User) : SocketResponse.Fail(result.StatusCode, result.Error!);
}
