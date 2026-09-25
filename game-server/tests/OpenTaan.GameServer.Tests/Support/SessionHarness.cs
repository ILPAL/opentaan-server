using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Transport;

namespace OpenTaan.Tests.GameServer;

/// <summary>A running session and the client connected to it.</summary>
internal sealed record Connection(ClientSession Session, LoopbackClient Client, Task<CloseReason> Closed) : IAsyncDisposable
{
    /// <summary>Why the session ended, failing the test if it has not ended within a few seconds.</summary>
    public async Task<CloseReason> ClosedReason() => await Closed.WaitAsync(TimeSpan.FromSeconds(10));

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

/// <summary>Runs real sessions over loopback sockets.</summary>
internal static class SessionHarness
{
    private static long _nextId;

    /// <summary>What sessions share, with a store and settings a test may choose.</summary>
    public static SessionServices Services(
        bool requireRegistration = true,
        IReadOnlyList<string>? gameMasters = null,
        IAccountStore? store = null,
        GameServerOptions? options = null,
        HandshakeKeySource? handshakeKeys = null)
    {
        var accounts = new AccountHandlers(
            store ?? new InMemoryAccountStore(),
            Options.Create(new AccountOptions { RequireRegistration = requireRegistration, GameMasters = gameMasters ?? [] }),
            NullLogger<AccountHandlers>.Instance);

        var names = new PacketNames();
        var dispatcher = new PacketDispatcher(accounts, new CharacterHandlers(), names, NullLogger<PacketDispatcher>.Instance);

        return new SessionServices(
            dispatcher,
            names,
            options ?? new GameServerOptions(),
            handshakeKeys ?? (() => HandshakeKeys.Generate(RandomNumberGenerator.Create())),
            new LoggedInAccounts(),
            NullLogger<ClientSession>.Instance);
    }

    /// <summary>
    /// Opens a loopback connection, runs a session on the server's end, and reads the
    /// handshake on the client's.
    /// </summary>
    /// <param name="services">What the session uses.</param>
    /// <param name="stopping">Stands in for server shutdown.</param>
    /// <param name="wrap">Wraps the server's stream, to stand in for a misbehaving network.</param>
    public static async Task<Connection> ConnectAsync(
        SessionServices services,
        CancellationToken stopping,
        Func<Stream, Stream>? wrap = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connecting = clientSocket.ConnectAsync((IPEndPoint)listener.LocalEndpoint, stopping);
        var serverSocket = await listener.AcceptSocketAsync(stopping);
        await connecting;
        listener.Stop();

        Stream stream = new NetworkStream(serverSocket, ownsSocket: true);
        var session = new ClientSession(Interlocked.Increment(ref _nextId), serverSocket.RemoteEndPoint, wrap?.Invoke(stream) ?? stream, services);
        var closed = Task.Run(() => session.RunAsync(stopping), CancellationToken.None);

        var client = await LoopbackClient.AttachAsync(clientSocket);
        return new Connection(session, client, closed);
    }
}
