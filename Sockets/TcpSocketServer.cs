using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MusicalEncryptionServer.Sockets;

/// <summary>
/// Listens for TCP connections on <see cref="Port"/> and hands each one off to
/// its own <see cref="ClientSession"/> running on its own task, so any number
/// of clients can be connected at the same time — the listener never binds to
/// one particular client's IP (it binds <see cref="IPAddress.Any"/> instead,
/// which is also what lets another machine on the LAN reach it later).
/// </summary>
public class TcpSocketServer : BackgroundService
{
    public const int Port = 5300;

    private readonly RequestRouter _router;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TcpSocketServer> _logger;

    public TcpSocketServer(RequestRouter router, ILoggerFactory loggerFactory, ILogger<TcpSocketServer> logger)
    {
        _router = router;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        _logger.LogInformation("Socket server listening on 0.0.0.0:{Port}", Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken); // one task per client — never blocks accepting the next one
            }
        }
        catch (OperationCanceledException)
        {
            // server shutting down
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var session = new ClientSession(client, _router, _loggerFactory.CreateLogger<ClientSession>());
        try
        {
            await session.RunAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in client session.");
        }
    }
}
