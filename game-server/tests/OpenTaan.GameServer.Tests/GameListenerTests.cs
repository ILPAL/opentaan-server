using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>The listener, on a real port chosen by the system.</summary>
public sealed class GameListenerTests
{
    [Fact]
    public async Task EveryConnectionGetsItsOwnSessionAndHandshake()
    {
        await using var server = await RunningServer.StartAsync(new GameServerOptions());

        await using var first = await server.ConnectAsync();
        await using var second = await server.ConnectAsync();

        Assert.NotEqual(first.Greeting, second.Greeting);

        await second.SendAsync(new PqExistCharacter("Somebody"));
        await first.SendAsync(new PqExistCharacter("Somebody"));
        await first.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead);
        await second.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead);
    }

    [Fact]
    public async Task AConnectionPastTheLimitForItsAddressIsClosedWithoutAGreeting()
    {
        await using var server = await RunningServer.StartAsync(new GameServerOptions { MaxConnectionsPerAddress = 1 });

        await using var admitted = await server.ConnectAsync();
        using var refused = await server.ConnectSocketAsync();

        var buffer = new byte[16];
        var received = await refused.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, received);
    }

    [Fact]
    public async Task StoppingTheServerClosesOpenConnections()
    {
        var server = await RunningServer.StartAsync(new GameServerOptions());
        await using var client = await server.ConnectAsync();

        await server.DisposeAsync();

        Assert.True(await client.IsClosedByServerAsync());
    }

    private sealed class RunningServer : IAsyncDisposable
    {
        private readonly GameListener _listener;
        private readonly IPEndPoint _endPoint;

        private RunningServer(GameListener listener, IPEndPoint endPoint)
        {
            _listener = listener;
            _endPoint = endPoint;
        }

        public static async Task<RunningServer> StartAsync(GameServerOptions options)
        {
            options.BindAddress = IPAddress.Loopback.ToString();
            options.Port = 0;

            var listener = new GameListener(
                Options.Create(options),
                SessionHarness.Services(options: options),
                NullLogger<GameListener>.Instance);

            await listener.StartAsync(CancellationToken.None);
            return new RunningServer(listener, await listener.BoundEndPoint.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        public async Task<Socket> ConnectSocketAsync()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(_endPoint);
            return socket;
        }

        public async Task<LoopbackClient> ConnectAsync() =>
            await LoopbackClient.AttachAsync(await ConnectSocketAsync());

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            _listener.Dispose();
        }
    }
}
