using System.Buffers;
using System.Buffers.Binary;
using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Connection;
using OpenTaan.Transport;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// How a session treats traffic it cannot serve, and how it ends. Every case that
/// cannot be answered must close the connection: the client never re-sends, so a
/// request dropped in silence would leave it waiting.
/// </summary>
public sealed class SessionTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HeartbeatsAndTelemetryAreAcceptedAndNeverAnswered()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        await connection.Client.SendBodyAsync(Encoded.UInt32(PqKeepAlive.RegistryId));
        await connection.Client.SendBodyAsync((byte[])[.. Encoded.UInt32(PcLogging.RegistryId), 0x01, 0x02, 0x03]);
        await connection.Client.SendAsync(new PqExistCharacter("Somebody"));

        // Had either of the first two been answered, that answer would arrive first.
        Assert.Equal(new PrExistCharacter(false), await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead));
    }

    [Fact]
    public async Task APacketWithNoHandlerClosesTheConnection()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        await connection.Client.SendBodyAsync(Encoded.UInt32(0x0BADF00D));

        Assert.Equal(CloseReason.UnhandledPacket, await connection.ClosedReason());
        Assert.True(await connection.Client.IsClosedByServerAsync());
    }

    [Fact]
    public async Task ABodyItsCodecRefusesClosesTheConnection()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        // A check request whose address claims more characters than follow.
        await connection.Client.SendBodyAsync((byte[])[.. Encoded.UInt32(PqCheckAccount.RegistryId), 0x40, 0x00, 0x00, 0x00, 0x61, 0x00]);

        Assert.Equal(CloseReason.MalformedPacket, await connection.ClosedReason());
    }

    [Fact]
    public async Task ABodyTooShortForARegistryIdClosesTheConnection()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        await connection.Client.SendBodyAsync(new byte[] { 0x01, 0x02 });

        Assert.Equal(CloseReason.MalformedPacket, await connection.ClosedReason());
    }

    [Fact]
    public async Task AFrameWithAWrongChecksumClosesTheConnection()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        var frame = connection.Client.Frame(Encoded.UInt32(PqKeepAlive.RegistryId));
        frame[^1] ^= 0x01;
        await connection.Client.SendRawAsync(frame);

        Assert.Equal(CloseReason.InvalidFrame, await connection.ClosedReason());
    }

    [Fact]
    public async Task AFrameDeclaringALengthNoClientCouldSendClosesTheConnectionAtOnce()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        // A header alone, declaring far more than the receive window. Waiting for the
        // rest would hold the connection open until something killed it from outside.
        var header = new byte[TransportLimits.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, OpenTaanCipher.EncodeHeader(connection.Client.NextSendSeed, 0x00FF_FFFF));
        await connection.Client.SendRawAsync(header);

        Assert.Equal(CloseReason.InvalidFrame, await connection.ClosedReason());
    }

    [Fact]
    public async Task FramesSplitAcrossReadsAndFramesSharingOneAreBothRead()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        byte[] first = connection.Client.Frame(Encoded.Bytes(new PqExistCharacter("One").Write));
        byte[] second = connection.Client.Frame(Encoded.Bytes(new PqExistCharacter("Two").Write));
        byte[] both = [.. first, .. second];

        await connection.Client.SendRawAsync(both.AsMemory(0, 3));
        await Task.Delay(50, Cancel);
        await connection.Client.SendRawAsync(both.AsMemory(3));

        await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead);
        await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead);
    }

    [Fact]
    public async Task ASilentConnectionIsClosedAfterTheIdleTimeout()
    {
        var options = new GameServerOptions { IdleTimeout = TimeSpan.FromMilliseconds(200) };
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(options: options), Cancel);

        Assert.Equal(CloseReason.IdleTimeout, await connection.ClosedReason());
    }

    [Fact]
    public async Task HeartbeatsKeepAConnectionOpenPastTheIdleTimeout()
    {
        var options = new GameServerOptions { IdleTimeout = TimeSpan.FromMilliseconds(600) };
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(options: options), Cancel);

        for (var beat = 0; beat < 6; beat++)
        {
            await Task.Delay(200, Cancel);
            await connection.Client.SendBodyAsync(Encoded.UInt32(PqKeepAlive.RegistryId));
        }

        Assert.False(connection.Closed.IsCompleted);
        await connection.Client.SendAsync(new PqExistCharacter("Somebody"));
        await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead);
    }

    [Fact]
    public async Task AClientThatStopsReadingIsClosedWhenAFrameCannotLeave()
    {
        var options = new GameServerOptions { SendTimeout = TimeSpan.FromMilliseconds(200) };
        await using var connection = await SessionHarness.ConnectAsync(
            SessionHarness.Services(options: options),
            Cancel,
            stream => new StallingStream(stream, writesBeforeStalling: 1));

        await connection.Client.SendAsync(new PqExistCharacter("Somebody"));

        Assert.Equal(CloseReason.SendTimeout, await connection.ClosedReason());
    }

    [Fact]
    public async Task RepliesPilingUpPastTheQueueCloseTheConnectionWithoutBlockingTheSender()
    {
        var options = new GameServerOptions { SendQueueCapacity = 2 };
        await using var connection = await SessionHarness.ConnectAsync(
            SessionHarness.Services(options: options),
            Cancel,
            stream => new StallingStream(stream, writesBeforeStalling: 1));

        // One reply stalls on its way out, two wait behind it, and the fourth has nowhere to go.
        for (var request = 0; request < 4; request++)
        {
            await connection.Client.SendAsync(new PqExistCharacter("Somebody"));
        }

        Assert.Equal(CloseReason.SendQueueFull, await connection.ClosedReason());
    }

    [Fact]
    public async Task AReplyTooLargeForTheClientsWindowIsNeverSent()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        connection.Session.Send(new OversizedPacket());

        Assert.Equal(CloseReason.OversizedReply, await connection.ClosedReason());
        Assert.True(await connection.Client.IsClosedByServerAsync());
    }

    [Fact]
    public async Task AFailingHandlerClosesItsOwnConnection()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: new FailingStore()), Cancel);

        await connection.Client.SendAsync(new PqCheckAccount("player@example.test", 0));

        Assert.Equal(CloseReason.ServerError, await connection.ClosedReason());
    }

    [Fact]
    public async Task ShutdownClosesAnOpenConnection()
    {
        using var stopping = new CancellationTokenSource();
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), stopping.Token);

        await stopping.CancelAsync();

        Assert.Equal(CloseReason.ServerStopping, await connection.ClosedReason());
    }

    [Fact]
    public async Task OnlyTheFirstReasonForClosingIsKept()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        connection.Session.Close(CloseReason.SecondLogin);
        connection.Session.Close(CloseReason.IdleTimeout);

        Assert.Equal(CloseReason.SecondLogin, await connection.ClosedReason());
    }

    private sealed class OversizedPacket : IPacket
    {
        public void Write(IBufferWriter<byte> destination) =>
            destination.Write(new byte[TransportLimits.MaxEncryptedBody + 1]);
    }

    private sealed class FailingStore : IAccountStore
    {
        public ValueTask<Account?> FindAsync(string emailAddress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the store is down");

        public ValueTask<Account?> CreateAsync(string emailAddress, Secret passwordHash, Gender gender, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the store is down");

        public ValueTask<Account> FindOrCreateAsync(string emailAddress, Secret passwordHash, Gender gender, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the store is down");
    }
}
