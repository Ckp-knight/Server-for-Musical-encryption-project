using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace MusicalEncryptionServer.Sockets;

/// <summary>
/// Owns one connected client's lifetime: reads framed requests, routes them,
/// writes framed responses, until the client disconnects or something goes wrong.
/// </summary>
public class ClientSession
{
    private readonly TcpClient _client;
    private readonly RequestRouter _router;
    private readonly ILogger<ClientSession> _logger;

    public ClientSession(TcpClient client, RequestRouter router, ILogger<ClientSession> logger)
    {
        _client = client;
        _router = router;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var remote = _client.Client.RemoteEndPoint;
        _logger.LogInformation("Client connected: {Remote}", remote);

        try
        {
            var stream = _client.GetStream();
            while (!ct.IsCancellationRequested)
            {
                var frame = await MessageProtocol.ReadFrameAsync(stream, ct);
                if (frame is null)
                    break; // client closed the connection

                var request = SocketRequest.FromFrame(frame);
                var response = await _router.RouteAsync(request, ct);

                _logger.LogInformation("{Remote} -> {Action} => {Status}", remote, request.Action, response.StatusCode);

                await MessageProtocol.WriteFrameAsync(stream, response.ToFrame(), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // server shutting down — not an error
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
        {
            _logger.LogInformation("Client {Remote} disconnected: {Message}", remote, ex.Message);
        }
        finally
        {
            _client.Dispose();
            _logger.LogInformation("Client session ended: {Remote}", remote);
        }
    }
}
