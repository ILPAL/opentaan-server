using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Data;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Tests.GameServer;
using OpenTaan.Transport;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// Every recorded session, played by its participants against real sessions over
/// loopback sockets, for as long as the server handles what they send.
/// </summary>
/// <remarks>
/// <para>
/// Each participant gets a connection of its own, greeted with the handshake values the
/// session recorded, so the greeting itself is compared byte for byte along with every
/// reply. The recorded bodies are sent as they are; the frames around them are made
/// with this run's seeds, which the transport replay checks separately.
/// </para>
/// <para>
/// A participant is followed until it sends a packet this server does not handle yet,
/// or one whose answer goes to someone else: both belong to later stages, so the
/// participant's remaining steps are not played. Accounts are shared by everyone in the
/// session, as they are in the server.
/// </para>
/// <para>
/// One kind of step is skipped rather than played: a login the recording refused
/// because too many had failed. The login throttle arrives with the database, so this
/// server would answer that step on its merits. The steps after it are still played,
/// and still have to match.
/// </para>
/// <para>
/// A recorded answer to a request this server handles can hold packets of later stages.
/// A recorded packet this server has no codec for is not expected; every other one is,
/// in order.
/// </para>
/// <para>
/// The lobby's player list is not compared, in either direction. The recordings list
/// every lobby as empty and never add or remove a player, where this server lists the
/// players in it and tells them of each one who comes or goes, to participants whose
/// steps may be anywhere in the session. Who is listed is checked through real
/// connections in the game server's own tests.
/// </para>
/// <para>
/// Two kinds of recorded value are adjusted before comparing, and only those. A new
/// character's appearance carries the account's gender, where the recordings carry 0, a
/// placeholder of the server they were recorded from; the client draws a character's
/// default parts by that gender. And the channel list carries no level range, where the
/// recordings carry one that the server they were recorded from never applied: their
/// new characters enter channels below its minimum. This server advertises only what it
/// applies. Each recorded answer is decoded, those values set, and the answer encoded
/// again, so every other byte is still compared as recorded.
/// </para>
/// <para>
/// One recorded answer is replaced by design: a channel move answered with a reconnect.
/// This server keeps every channel on the connection the player already has, so it
/// answers that the player entered the channel, and the participant's recorded steps
/// after it, which reconnect, are not played.
/// </para>
/// </remarks>
public sealed class SessionReplayTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // The starting values the sessions were recorded with: placeholders, not a design.
    private static readonly CharacterOptions RecordedCharacterOptions = new()
    {
        CreatorParts = SessionHarness.ShippedCharacterOptions().CreatorParts,
        Start = new CharacterStart
        {
            ExperienceCeiling = 300,
            Gold = 1_000_000,
            RankPlacement = 1,
            RankScore = 1,
            LeagueMoney = 1,
            Wins = 1,
            Losses = 1,
            ForceMedals = 1,
            KillMedals = 1,
            SupportMedals = 1,
        },
    };

    // Every packet this server has a codec for, by registry id.
    private static readonly HashSet<uint> Implemented =
    [
        .. from type in typeof(ProtocolAssemblyMarker).Assembly.GetExportedTypes()
           let wireClass = type.GetCustomAttribute<WireClassAttribute>(inherit: false)
           where wireClass is not null
           select RegistryIds.Of(wireClass.Name),
    ];

    [Fact]
    public async Task EveryParticipantIsAnsweredAsRecordedUntilItLeavesThisStage()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var failures = new List<string>();
        var compared = 0;

        foreach (var session in WireContract.Sessions)
        {
            compared += await ReplayAsync(session, failures);
        }

        Assert.Empty(failures);
        Assert.True(compared > 0, "the corpus was present but no step of it was in this stage");
    }

    private static async Task<int> ReplayAsync(Session session, List<string> failures)
    {
        CryptoParameters? nextHandshake = null;
        var services = SessionHarness.Services(
            requireRegistration: session.World?.Authentication ?? false,
            gameMasters: session.World?.GmAccountEmails,
            handshakeKeys: () => new HandshakeKeys(nextHandshake!.FirstSeed, nextHandshake.SecondSeed, nextHandshake.Shift),
            characterOptions: RecordedCharacterOptions,
            channelOptions: RecordedChannelOptions(session));

        var connections = new Dictionary<string, Connection>(StringComparer.Ordinal);
        var compared = 0;

        try
        {
            for (var step = 0; step < session.Exchanges.Count; step++)
            {
                var exchange = session.Exchanges[step];
                var participant = exchange.From ?? string.Empty;
                var where = $"{session.Origin} step {step}";

                if (exchange.Event == "connect")
                {
                    await Retire(connections, participant);

                    // A connection with no handshake of its own is not a game client's.
                    nextHandshake = HandshakeFor(session, step, participant);
                    if (nextHandshake is not null)
                    {
                        connections[participant] = await SessionHarness.ConnectAsync(services, Cancel);
                    }

                    continue;
                }

                if (!connections.TryGetValue(participant, out var connection))
                {
                    continue;
                }

                if (exchange.Event == "handshake")
                {
                    Compare(failures, where, exchange.Frames[0].Body(), connection.Client.GreetingBody);
                    compared++;
                    continue;
                }

                if (exchange.Inbound is not { } inbound)
                {
                    continue;
                }

                var body = Convert.FromHexString(inbound);
                if (!services.Dispatcher.Routes(BinaryPrimitives.ReadUInt32LittleEndian(body))
                    || exchange.Frames.Any(frame => frame.To != participant))
                {
                    await Retire(connections, participant);
                    continue;
                }

                if (IsThrottleRefusal(exchange))
                {
                    continue;
                }

                await connection.Client.SendBodyAsync(body);
                var reconnects = false;
                foreach (var recorded in exchange.Frames.Select(frame => frame.Body()).Where(IsImplemented).Where(body => !IsLobbyPlayerList(body)))
                {
                    var actual = await ReceiveComparedAsync(connection);
                    if (IsReconnect(recorded))
                    {
                        CompareEnteredInstead(failures, where, recorded, actual);
                        reconnects = true;
                    }
                    else
                    {
                        Compare(failures, where, Adjusted(recorded), actual);
                    }

                    compared++;
                }

                if (reconnects)
                {
                    await Retire(connections, participant);
                }
            }
        }
        finally
        {
            foreach (var participant in connections.Keys.ToArray())
            {
                await Retire(connections, participant);
            }
        }

        return compared;
    }

    // The handshake recorded for this connection: the participant's next one, before
    // any later connection of theirs.
    private static CryptoParameters? HandshakeFor(Session session, int connect, string participant) =>
        session.Exchanges
            .Skip(connect + 1)
            .Where(exchange => (exchange.From ?? string.Empty) == participant)
            .TakeWhile(exchange => exchange.Event != "connect")
            .Select(exchange => exchange.Crypto)
            .FirstOrDefault(crypto => crypto is not null);

    // The settings the sessions were recorded with: five ordinary channels and an arena
    // one, all at the address the session records. The recordings show every channel
    // empty while three players are in the first, so the capacity is large enough to
    // round them down to 0%.
    private static ChannelOptions RecordedChannelOptions(Session session) => new()
    {
        Server = new ChannelServer
        {
            Name = "Alpha",
            Type = 1,
            PublicAddress = session.AdvertisedAddress?.Ip,
            PublicPort = session.AdvertisedAddress?.Port,
        },
        List =
        [
            .. Enumerable.Range(1, 5).Select(id => new ChannelSettings { Id = (ushort)id, Name = $"Channel {id}", Capacity = 1000 }),
            new ChannelSettings { Id = 6, Name = "Arena", Mode = 7, Capacity = 1000 },
        ],
    };

    private static bool IsImplemented(byte[] body) => Implemented.Contains(BinaryPrimitives.ReadUInt32LittleEndian(body));

    private static bool IsLobbyPlayerList(byte[] body) =>
        BinaryPrimitives.ReadUInt32LittleEndian(body) is var id
        && (id == PcLobbyInitUserList.RegistryId || id == PcLobbyUpdateUserList.RegistryId);

    // The participant's next body that the replay compares, past any lobby player list.
    private static async Task<byte[]> ReceiveComparedAsync(Connection connection)
    {
        while (true)
        {
            var body = await connection.Client.ReceiveBodyAsync();
            if (!IsLobbyPlayerList(body))
            {
                return body;
            }
        }
    }

    // The recorded answer with the adjustments the remarks above describe.
    private static byte[] Adjusted(byte[] recorded)
    {
        var adjusted = new ArrayBufferWriter<byte>();
        switch (BinaryPrimitives.ReadUInt32LittleEndian(recorded))
        {
            case var id when id == PrNewCharacter.RegistryId
                             && PrNewCharacter.TryRead(recorded).Packet is { Catalog: { } catalog } answer:
                // Each new character's appearance gets its account's gender, which the
                // character's own description carries.
                var appearances = catalog.Appearances
                    .Select((appearance, position) => appearance with { Gender = catalog.Characters[position].Gender })
                    .ToArray();
                (answer with { Catalog = new CharacterCatalog(catalog.Characters, appearances) }).Write(adjusted);
                break;

            case var id when id == PrGetChannelList.RegistryId
                             && PrGetChannelList.TryRead(recorded).Packet is { } list:
                new PrGetChannelList(WithoutLevelRanges(list.Channels)).Write(adjusted);
                break;

            default:
                return recorded;
        }

        return adjusted.WrittenSpan.ToArray();
    }

    private static DataNode WithoutLevelRanges(DataNode node) => node with
    {
        Attributes = [.. node.Attributes.Select(attribute => attribute.Name is "minLevel" or "maxLevel" ? attribute with { Value = "0" } : attribute)],
        Children = [.. node.Children.Select(WithoutLevelRanges)],
    };

    private static bool IsReconnect(byte[] recorded) =>
        BinaryPrimitives.ReadUInt32LittleEndian(recorded) == PrChannelMoveTo.RegistryId
        && PrChannelMoveTo.TryRead(recorded).Packet is { Result: ChannelMoveResult.Reconnect };

    // A move this server answers as entered where the recording reconnects: the same
    // channel, entered, described.
    private static void CompareEnteredInstead(List<string> failures, string where, byte[] recorded, byte[] actual)
    {
        var expected = PrChannelMoveTo.TryRead(recorded).Packet!;
        if (PrChannelMoveTo.TryRead(actual).Packet is not { Result: ChannelMoveResult.Entered, Channel: { } channel } answer
            || answer.ChannelId != expected.ChannelId
            || channel.ChannelId != expected.ChannelId)
        {
            failures.Add($"{where}: expected the move to channel {expected.ChannelId} answered as entered, got {Convert.ToHexString(actual)}");
        }
    }

    private static bool IsThrottleRefusal(Exchange exchange) =>
        exchange.Frames.Count == 1
        && PrUserLogin.TryRead(exchange.Frames[0].Body()).Packet is PrUserLoginFailed { Reason: LoginRefusalReason.Penalty };

    private static void Compare(List<string> failures, string where, byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            failures.Add($"{where}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
        }
    }

    private static async Task Retire(Dictionary<string, Connection> connections, string participant)
    {
        if (connections.Remove(participant, out var connection))
        {
            await connection.DisposeAsync();
        }
    }
}
