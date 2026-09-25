using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Sessions;

namespace OpenTaan.GameServer;

/// <summary>
/// Accepts game client connections and runs a <see cref="ClientSession"/> for each.
/// </summary>
/// <remarks>
/// Each session runs as its own task and holds no thread while it waits for the
/// network, so the number of connections is bounded by the configured limits rather
/// than by threads. On shutdown the listener stops accepting, every session is told to
/// close, and shutdown waits for all of them together, within the host's deadline.
/// </remarks>
public sealed partial class GameListener(
    IOptions<GameServerOptions> options,
    SessionServices services,
    ILogger<GameListener> logger) : BackgroundService
{
    private readonly TaskCompletionSource<IPEndPoint> _bound = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The address the listener bound, once it has; with port 0, the port it was given.</summary>
    public Task<IPEndPoint> BoundEndPoint => _bound.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var limiter = new ConnectionLimiter(settings.MaxConnections, settings.MaxConnectionsPerAddress);
        var sessions = new ConcurrentDictionary<long, Task>();
        long nextId = 0;

        var listener = new TcpListener(IPAddress.Parse(settings.BindAddress), settings.Port);
        listener.Start();
        var bound = (IPEndPoint)listener.LocalEndpoint;
        _bound.TrySetResult(bound);
        LogListening(logger, bound.Address, bound.Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var socket = await listener.AcceptSocketAsync(stoppingToken).ConfigureAwait(false);
                var remote = (IPEndPoint)socket.RemoteEndPoint!;

                var admission = limiter.TryAdmit(remote.Address);
                if (admission != Admission.Admitted)
                {
                    // Closed at once: the client shows a failure to connect, rather than
                    // waiting on a greeting that will never come.
                    LogRefused(logger, remote, admission);
                    socket.Dispose();
                    continue;
                }

                // Replies are small and answer a waiting client; sending each at once
                // matters more than packing them.
                socket.NoDelay = true;

                var id = ++nextId;
                var session = Task.Run(() => RunSessionAsync(id, socket, remote, limiter, stoppingToken), CancellationToken.None);
                sessions[id] = session;
                _ = session.ContinueWith(
                    _ => sessions.TryRemove(id, out Task? _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            listener.Stop();
            await Task.WhenAll(sessions.Values).ConfigureAwait(false);
        }
    }

    private async Task RunSessionAsync(long id, Socket socket, IPEndPoint remote, ConnectionLimiter limiter, CancellationToken stoppingToken)
    {
        try
        {
            var session = new ClientSession(id, remote, new NetworkStream(socket, ownsSocket: true), services);
            await session.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // One connection's failure is logged and ends that connection, never the listener.
            LogSessionFailed(logger, exception, id);
        }
        finally
        {
            limiter.Release(remote.Address);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening for game clients on {Address}:{Port}")]
    private static partial void LogListening(ILogger logger, IPAddress address, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a connection from {RemoteEndPoint}: {Admission}")]
    private static partial void LogRefused(ILogger logger, IPEndPoint remoteEndPoint, Admission admission);

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection {ConnectionId} failed")]
    private static partial void LogSessionFailed(ILogger logger, Exception exception, long connectionId);
}
