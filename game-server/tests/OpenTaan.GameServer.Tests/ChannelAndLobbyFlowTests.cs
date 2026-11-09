using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Lobby;
using OpenTaan.GameServer.PlayTime;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.PlayTime;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Cash, channels and the lobby end to end, through real sessions on loopback sockets.
/// Every value here is invented.
/// </summary>
public sealed class ChannelAndLobbyFlowTests
{
    private const uint StartingCash = 0x0A0B0C0D;

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Two ordinary channels with room for four each, and a closed one.
    private static ChannelOptions Channels(int capacity = 4) => new()
    {
        Server = new ChannelServer { Name = "Gamma", Type = 4, PublicAddress = "203.0.113.7", PublicPort = 2190 },
        List =
        [
            new ChannelSettings { Id = 11, Name = "Kênh A", Capacity = capacity },
            new ChannelSettings { Id = 12, Name = "Kênh B", Capacity = capacity },
            new ChannelSettings { Id = 13, Name = "Kênh C", Capacity = capacity, Disabled = true },
        ],
    };

    [Fact]
    public async Task EachAccountSlotIsAnsweredWithItsOwnCash()
    {
        await using var connection = await ConnectAsync();
        await LogInAsync(connection, "first@example.test", "second@example.test");

        await connection.Client.SendAsync(new PcUpdateCash(Cash: 0x55, AccountSlot: 1));

        Assert.Equal(new PcUpdateCash(StartingCash, 1), await connection.Client.ReceiveAsync<PcUpdateCash>(PcUpdateCash.TryRead));
    }

    [Fact]
    public async Task CashForASlotTheConnectionDoesNotHoldIsAnsweredForSlotZero()
    {
        await using var connection = await ConnectAsync();
        await LogInAsync(connection, "player@example.test");

        await connection.Client.SendAsync(new PcUpdateCash(Cash: 0, AccountSlot: 0x01000000));

        Assert.Equal(new PcUpdateCash(StartingCash, 0), await connection.Client.ReceiveAsync<PcUpdateCash>(PcUpdateCash.TryRead));
    }

    [Fact]
    public async Task CashAskedForBeforeLoginIsNoneForSlotZero()
    {
        await using var connection = await ConnectAsync();

        await connection.Client.SendAsync(new PcUpdateCash(Cash: 0, AccountSlot: 1));

        Assert.Equal(new PcUpdateCash(0, 0), await connection.Client.ReceiveAsync<PcUpdateCash>(PcUpdateCash.TryRead));
    }

    [Fact]
    public async Task AMoveIsAnsweredAsEnteredOnTheSameConnectionAndItsLobbyIsThatChannels()
    {
        await using var connection = await ConnectAsync();
        await PlayAsync(connection, "player@example.test", "Kimi");

        await connection.Client.SendAsync(new PqChannelMoveTo(Forced: false, ChannelId: 12, Context: 0, ClientVersionType: 0));
        var (_, answer) = await connection.Client.ReceiveMoveAnswerAsync();

        Assert.Equal(
            (ChannelMoveResult.Entered, (ushort)12, new ConnectionAddress(0x077100CB, 2190), 0),
            (answer.Result, answer.ChannelId, answer.ServerAddress, answer.Characters.Count));
        Assert.Equal(
            new ChannelInfo("Kênh B", 12, ChannelSettings.NormalMode, new ConnectionAddress(0x077100CB, 2190), 25, 0, 0, 0, 0),
            answer.Channel);

        await connection.Client.SendAsync(new PqLobbyEnter(FromChannelMove: 0, ArenaTeamMode: 0xFF, ArenaTeamKey: 0xFFFFFFFF));
        Assert.Equal(new PrLobbyEnter(12, LobbyEnterResult.Entered), await connection.Client.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead));
    }

    [Fact]
    public async Task AnEnteredMoveIsPrecededByPlayTimeWithNoLimit()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await SessionHarness.ConnectAsync(Services(time: new FixedTime(now)), Cancel);
        var character = await PlayAsync(connection, "player@example.test", "Kimi");

        await connection.Client.SendAsync(new PqChannelMoveTo(false, 11, 0, 0));
        var (grant, answer) = await connection.Client.ReceiveMoveAnswerAsync();

        Assert.Equal(ChannelMoveResult.Entered, answer.Result);
        Assert.NotNull(grant);
        Assert.Equal(
            new PcNotifyPlayableTime(0, (uint)now.ToUnixTimeSeconds(), 0, uint.MaxValue, uint.MaxValue, 0),
            Assert.Single(grant.Limits));
        Assert.Equal(new PcPlayableTimeCtrl(character, 0, Counting: true), Assert.Single(grant.Counters));
    }

    [Fact]
    public async Task AConfiguredLimitIsGrantedToEveryAccountSlotAndCharacter()
    {
        var services = Services(playTime: new PlayTimeOptions { AllowedSeconds = 3600 });
        await using var connection = await SessionHarness.ConnectAsync(services, Cancel);
        await LogInAsync(connection, "first@example.test", "second@example.test");
        var first = await CreateAsync(connection, 0, "Kimi");
        var second = await CreateAsync(connection, 1, "Rồng");
        await connection.Client.SendAsync(new PqSelectCharacter([first, second]));
        await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);

        await connection.Client.SendAsync(new PqChannelMoveTo(false, 11, 0, 0));
        var (grant, _) = await connection.Client.ReceiveMoveAnswerAsync();

        Assert.NotNull(grant);
        Assert.Equal(
            [(0, 0u, 3600u, 3600u), (1, 0u, 3600u, 3600u)],
            grant.Limits.Select(limit => ((int)limit.SlotIndex, limit.PlayedSeconds, limit.AllowedSeconds, limit.MaxPlaySeconds)));
        Assert.Equal([first, second], grant.Counters.Select(counter => counter.CharacterId));
    }

    [Theory]
    [InlineData((ushort)13, 0u)]
    [InlineData((ushort)14, 0u)]
    [InlineData((ushort)0, 0x0102u)]
    public async Task AMoveToAClosedOrUnknownChannelOrAFollowIsRefusedAsClosed(ushort channelId, uint context)
    {
        await using var connection = await ConnectAsync();
        await PlayAsync(connection, "player@example.test", "Kimi");

        await connection.Client.SendAsync(new PqChannelMoveTo(context != 0, channelId, context, 0));
        var (grant, answer) = await connection.Client.ReceiveMoveAnswerAsync();

        Assert.Equal((ChannelMoveResult.ChannelClosed, channelId, (ChannelInfo?)null), (answer.Result, answer.ChannelId, answer.Channel));
        Assert.Null(grant);
    }

    [Fact]
    public async Task AFullChannelIsRefusedUntilAConnectionInItCloses()
    {
        var services = Services(Channels(capacity: 1));
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(first, "first@example.test", "Kimi");
        await PlayAsync(second, "second@example.test", "Rồng");

        Assert.Equal(ChannelMoveResult.Entered, await MoveAsync(first, 11));
        Assert.Equal(ChannelMoveResult.ChannelFull, await MoveAsync(second, 11));

        await first.Client.DisposeAsync();
        await first.ClosedReason();

        Assert.Equal(ChannelMoveResult.Entered, await MoveAsync(second, 11));
    }

    [Fact]
    public async Task TheChannelListShowsHowFullEachChannelIsNow()
    {
        await using var connection = await ConnectAsync();
        await PlayAsync(connection, "player@example.test", "Kimi");
        await MoveAsync(connection, 11);
        await MoveAsync(connection, 12);

        await connection.Client.SendAsync(new PqGetChannelList());
        var server = Assert.Single((await connection.Client.ReceiveAsync<PrGetChannelList>(PrGetChannelList.TryRead)).Channels.Children);

        Assert.Equal(
            [("11", "0", "0"), ("12", "25", "0"), ("13", "0", "1")],
            server.Children.Select(channel => (channel.Attribute("id"), channel.Attribute("fill"), channel.Attribute("disable"))));
        Assert.All(server.Children, channel => Assert.Equal(("203.0.113.7", "2190"), (channel.Attribute("ip"), channel.Attribute("port"))));
    }

    [Fact]
    public async Task AMoveBeforeCharactersAreSelectedClosesTheConnection()
    {
        await using var connection = await ConnectAsync();
        await LogInAsync(connection, "player@example.test");

        await connection.Client.SendAsync(new PqChannelMoveTo(false, 11, 0, 0));

        Assert.Equal(CloseReason.NoCharacterSelected, await connection.ClosedReason());
    }

    [Fact]
    public async Task ALobbyEntryWithoutAMoveEntersTheFirstChannelsLobby()
    {
        await using var connection = await ConnectAsync();
        var character = await PlayAsync(connection, "player@example.test", "Kimi");

        await connection.Client.SendAsync(new PqLobbyEnter(1, 0, 1));

        Assert.Equal(new PrLobbyEnter(11, LobbyEnterResult.Entered), await connection.Client.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead));
        Assert.True((await connection.Client.ReceiveAsync<PcLobbyInitRoomList>(PcLobbyInitRoomList.TryRead)) is not null);
        var users = await connection.Client.ReceiveAsync<PcLobbyInitUserList>(PcLobbyInitUserList.TryRead);
        Assert.Equal(character, Assert.Single(users.Characters).CharacterId);
    }

    [Fact]
    public async Task ALobbyEntryWithoutAMoveIsRefusedWhenTheFirstChannelIsClosed()
    {
        var channels = Channels();
        channels.List[0].Disabled = true;
        await using var connection = await SessionHarness.ConnectAsync(Services(channels), Cancel);
        await PlayAsync(connection, "player@example.test", "Kimi");

        await connection.Client.SendAsync(new PqLobbyEnter(1, 0, 1));

        Assert.Equal(new PrLobbyEnter(11, LobbyEnterResult.Failed), await connection.Client.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead));
    }

    [Fact]
    public async Task ALobbyEntryBeforeCharactersAreSelectedIsRefused()
    {
        await using var connection = await ConnectAsync();
        await LogInAsync(connection, "player@example.test");

        await connection.Client.SendAsync(new PqLobbyEnter(0, 0xFF, 0xFFFFFFFF));

        Assert.Equal(new PrLobbyEnter(0, LobbyEnterResult.Failed), await connection.Client.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead));
    }

    [Fact]
    public async Task LeavingTheLobbyIsAnsweredEvenOutsideOne()
    {
        await using var connection = await ConnectAsync();

        await connection.Client.SendAsync(new PqLobbyLeave());

        Assert.NotNull(await connection.Client.ReceiveAsync<PrLobbyLeave>(PrLobbyLeave.TryRead));
    }

    [Fact]
    public async Task APictureOfASelectedCharacterIsKeptAsItsPngFile()
    {
        var folder = System.IO.Directory.CreateTempSubdirectory();
        try
        {
            await using var connection = await SessionHarness.ConnectAsync(Services(folder: folder.FullName), Cancel);
            var character = await PlayAsync(connection, "player@example.test", "Kimi");

            await connection.Client.SendAsync(new PcUploadWebImage(character, Png));
            await connection.Client.SendAsync(new PcUploadWebImage(character, [0x42, 0x4D, 0x01]));
            await connection.Client.SendAsync(new PcUploadWebImage(character + 1, Png));
            await HandledSoFarAsync(connection);

            Assert.Equal(Png, await File.ReadAllBytesAsync(Path.Combine(folder.FullName, $"{character}.png"), Cancel));
            Assert.Equal([0x42, 0x4D, 0x01], await File.ReadAllBytesAsync(Path.Combine(folder.FullName, $"{character}.bin"), Cancel));
            Assert.Equal(2, folder.GetFiles().Length);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static SessionServices Services(
        ChannelOptions? channels = null,
        string? folder = null,
        PlayTimeOptions? playTime = null,
        TimeProvider? time = null) =>
        SessionHarness.Services(
            requireRegistration: false,
            store: new InMemoryAccountStore(StartingCash),
            channelOptions: channels ?? Channels(),
            portraitOptions: new PortraitOptions { Folder = folder },
            playTimeOptions: playTime,
            time: time);

    private static Task<Connection> ConnectAsync() => SessionHarness.ConnectAsync(Services(), Cancel);

    private static async Task<ChannelMoveResult> MoveAsync(Connection connection, ushort channelId)
    {
        await connection.Client.SendAsync(new PqChannelMoveTo(false, channelId, 0, 0));
        return (await connection.Client.ReceiveMoveAnswerAsync()).Answer.Result;
    }

    // Requests are handled one at a time, in order: once a later one is answered, every
    // earlier one has been handled.
    private static async Task HandledSoFarAsync(Connection connection)
    {
        await connection.Client.SendAsync(new PqLobbyLeave());
        await connection.Client.ReceiveAsync<PrLobbyLeave>(PrLobbyLeave.TryRead);
    }

    private static async Task LogInAsync(Connection connection, params string[] emailAddresses)
    {
        await connection.Client.SendAsync(new PqUserLogin(
            [.. emailAddresses.Select(address => new LoginAccountInfo(address, new Secret("digest"), 0, string.Empty))],
            new ConnectionAddress(0, 0),
            0));
        Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    private static async Task<uint> CreateAsync(Connection connection, byte slot, string name)
    {
        await connection.Client.SendAsync(new PqNewCharacter(slot, name, new AppearanceSlot[AppearanceSlot.Count]));
        return Assert.Single((await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead)).Catalog!.Characters).CharacterId;
    }

    // Logs in, creates a character and selects it, as the client does before its channel screen.
    private static async Task<uint> PlayAsync(Connection connection, string emailAddress, string name)
    {
        await LogInAsync(connection, emailAddress);

        await connection.Client.SendAsync(new PqNewCharacter(0, name, new AppearanceSlot[AppearanceSlot.Count]));
        var character = Assert.Single((await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead)).Catalog!.Characters);

        await connection.Client.SendAsync(new PqSelectCharacter([character.CharacterId]));
        await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);
        return character.CharacterId;
    }
}
