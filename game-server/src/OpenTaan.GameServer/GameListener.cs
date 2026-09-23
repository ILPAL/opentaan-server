using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace OpenTaan.GameServer;

/// <summary>
/// Accepts game client connections. Until the transport layer exists, each
/// connection is logged and closed, which is enough for health checks and for
/// seeing the service start in docker-compose.
/// </summary>
public sealed partial class GameListener(IOptions<GameServerOptions> options, ILogger<GameListener> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var listener = new TcpListener(IPAddress.Parse(settings.BindAddress), settings.Port);
        listener.Start();
        LogListening(logger, settings.BindAddress, settings.Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stoppingToken);
                LogAccepted(logger, client.Client.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            listener.Stop();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening for game clients on {Address}:{Port}")]
    private static partial void LogListening(ILogger logger, string address, int port);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Accepted {RemoteEndPoint} and closed it: the transport layer is not implemented yet")]
    private static partial void LogAccepted(ILogger logger, EndPoint? remoteEndPoint);
}
