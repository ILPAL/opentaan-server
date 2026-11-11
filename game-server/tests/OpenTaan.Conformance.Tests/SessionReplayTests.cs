using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Data;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;
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
/// What a step sends is compared on the connection it goes to: the answer on the
/// sender's, and what others are told, such as a slot taken in their room, on theirs.
/// A participant is followed until it sends a packet this server does not handle yet,
/// or one whose answer goes to a participant no longer followed: both belong to later
/// stages, so the participant's remaining steps are not played. One that leaves this
/// stage while in a room ends the whole session's replay there, since what everyone
/// else does from then on can depend on what it would have done in the room. Accounts
/// are shared by everyone in the session, as they are in the server.
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
/// The lobby's player list, and the changes to its room list, are not compared, in
/// either direction. The recordings list every lobby's players as empty and never add or
/// remove a player or change a room, where this server lists the players in a lobby and
/// tells everyone in it of each player who comes or goes and of each room that opens,
/// fills or closes, to participants whose steps may be anywhere in the session. These are
/// checked through real connections in the game server's own tests. The room list a
/// lobby is entered with is compared: every recorded one is empty, as no room exists yet.
/// </para>
/// <para>
/// A few kinds of recorded value are adjusted before comparing, and only those:
/// <list type="bullet">
///   <item>A character's appearance carries its account's gender, where the recordings
///   carry 0, a placeholder of the server they were recorded from; the client draws a
///   character's default parts by that gender. So does every new character's, and every
///   character's in a room's slots, which the recordings carry the same way.</item>
///   <item>The channel list carries no level range, where the recordings carry one that
///   the server they were recorded from never applied: their new characters enter
///   channels below its minimum. This server advertises only what it applies.</item>
///   <item>A room's reply carries empty team names, where the recordings carry
///   placeholders. The client reads them only in arena channels.</item>
///   <item>In a mission room, the reply to a player entering carries the mission's
///   difficulty, as the reply to its creator does. The recordings carry the player's slot
///   there, so a player entering was shown a difficulty the room did not have.</item>
///   <item>The start of a mission lists how each character starts, and only the
///   characters, where the recordings also list the mission's own side, closed slots the
///   client skips.</item>
/// </list>
/// Each recorded answer is decoded, those values set, and the answer encoded again, so
/// every other byte is still compared as recorded.
/// </para>
/// <para>
/// Two kinds of step end the session's replay, as this server answers them differently by
/// design. A host starting a match between teams is not answered yet. And a client
/// reporting a mission level ready before any level was asked for is not answered: the
/// recordings report level 0 ready first and ask for level 1 after, where a real client
/// asks for level 1 once it has loaded, and reports a level ready once it has been told the
/// level begins. The match's seed and its automatic card picks are drawn as the recordings
/// have them, 0 and the first card left.
/// </para>
/// <para>
/// One recorded answer is replaced by design: a channel move answered with a reconnect.
/// This server keeps every channel on the connection the player already has, so it
/// answers that the player entered the channel, and the participant's recorded steps
/// after it, which reconnect, are not played. The play time the recording grants on
/// the connection it reconnects with comes ahead of that answer instead, so it is
/// expected there, with no limit, as this server is set up by default.
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

    // What the replay does not compare; see the remarks.
    private static readonly HashSet<uint> NotCompared =
    [
        PcLobbyInitUserList.RegistryId,
        PcLobbyUpdateUserList.RegistryId,
        PcLobbyUpdateRoomList.RegistryId,
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
            channelOptions: RecordedChannelOptions(session),
            random: new RecordedChance());

        var connections = new Dictionary<string, Connection>(StringComparer.Ordinal);
        var recordedValues = new RecordedValues();
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
                var expected = exchange.Frames
                    .Select(frame => (To: frame.To ?? participant, Body: frame.Body()))
                    .Where(frame => IsImplemented(frame.Body) && !NotCompared.Contains(RegistryIdOf(frame.Body)))
                    .ToArray();

                if (!services.Dispatcher.Routes(RegistryIdOf(body)) || expected.Any(frame => !connections.ContainsKey(frame.To)))
                {
                    if (services.Rooms.RoomOf(connection.Session) is not null)
                    {
                        break;
                    }

                    await Retire(connections, participant);
                    continue;
                }

                if (IsThrottleRefusal(exchange))
                {
                    continue;
                }

                if (IsAnsweredDifferentlyByDesign(body, services.Rooms.RoomOf(connection.Session), recordedValues))
                {
                    break;
                }

                await connection.Client.SendBodyAsync(body);
                var reconnects = false;
                foreach (var (to, recorded) in expected)
                {
                    recordedValues.Learn(body, recorded);
                    if (to == participant && IsReconnect(recorded))
                    {
                        var (grant, answer) = await connection.Client.ReceiveMoveAnswerAsync();
                        CompareEnteredInstead(failures, where, recorded, grant, answer);
                        reconnects = true;
                    }
                    else
                    {
                        Compare(failures, where, recordedValues.Adjusted(recorded), await ReceiveComparedAsync(connections[to]));
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

    private static uint RegistryIdOf(byte[] body) => BinaryPrimitives.ReadUInt32LittleEndian(body);

    private static bool IsImplemented(byte[] body) => Implemented.Contains(RegistryIdOf(body));

    // The participant's next body that the replay compares, past any it does not.
    private static async Task<byte[]> ReceiveComparedAsync(Connection connection)
    {
        while (true)
        {
            var body = await connection.Client.ReceiveBodyAsync();
            if (!NotCompared.Contains(RegistryIdOf(body)))
            {
                return body;
            }
        }
    }

    private static DataNode WithoutLevelRanges(DataNode node) => node with
    {
        Attributes = [.. node.Attributes.Select(attribute => attribute.Name is "minLevel" or "maxLevel" ? attribute with { Value = "0" } : attribute)],
        Children = [.. node.Children.Select(WithoutLevelRanges)],
    };

    private static bool IsReconnect(byte[] recorded) =>
        RegistryIdOf(recorded) == PrChannelMoveTo.RegistryId
        && PrChannelMoveTo.TryRead(recorded).Packet is { Result: ChannelMoveResult.Reconnect };

    // A move this server answers as entered where the recording reconnects: the same
    // channel, entered, described, after a grant of play time with no limit.
    private static void CompareEnteredInstead(
        List<string> failures,
        string where,
        byte[] recorded,
        PlayTimeGrantReceived? grant,
        PrChannelMoveTo answer)
    {
        var expected = PrChannelMoveTo.TryRead(recorded).Packet!;
        if (answer is not { Result: ChannelMoveResult.Entered, Channel: { } channel }
            || answer.ChannelId != expected.ChannelId
            || channel.ChannelId != expected.ChannelId)
        {
            failures.Add($"{where}: expected the move to channel {expected.ChannelId} answered as entered, got {answer}");
        }

        if (grant is null || grant.Limits.Count == 0 || grant.Limits.Any(limit => limit.AllowedSeconds != uint.MaxValue))
        {
            failures.Add($"{where}: expected the entered move to grant play time with no limit first");
        }
    }

    // A host starting a match between teams, or a client reporting a mission level ready
    // that no recorded step has begun; see the remarks.
    private static bool IsAnsweredDifferentlyByDesign(byte[] body, ushort? roomNumber, RecordedValues recordedValues) =>
        RegistryIdOf(body) switch
        {
            var id when id == PqGameSessionStartGame.RegistryId => roomNumber is not { } number || !recordedValues.IsMissionRoom(number),
            var id when id == PqGameSessionMissionLevelReady.RegistryId => !recordedValues.HasBegun(PqGameSessionMissionLevelReady.TryRead(body).Packet!.Level),
            _ => false,
        };

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

    /// <summary>
    /// What one session's recordings say about its characters and rooms, learned as it is
    /// played, and the adjustments the remarks above describe.
    /// </summary>
    private sealed class RecordedValues
    {
        private readonly Dictionary<uint, Gender> _genders = [];
        private readonly Dictionary<ushort, byte> _missionDifficulties = [];
        private readonly HashSet<byte> _levelsBegun = [];

        /// <summary>Whether room <paramref name="number"/> was recorded as created to play missions.</summary>
        public bool IsMissionRoom(ushort number) => _missionDifficulties.ContainsKey(number);

        /// <summary>Whether a recorded step began mission level <paramref name="level"/>.</summary>
        public bool HasBegun(byte level) => _levelsBegun.Contains(level);

        /// <summary>Learns from <paramref name="recorded"/>, sent in answer to <paramref name="request"/>.</summary>
        public void Learn(byte[] request, byte[] recorded)
        {
            switch (RegistryIdOf(recorded))
            {
                case var id when id == PrNewCharacter.RegistryId
                                 && PrNewCharacter.TryRead(recorded).Packet is { Catalog: { } catalog }:
                    LearnGenders(catalog);
                    break;

                case var id when id == PrUserLogin.RegistryId
                                 && PrUserLogin.TryRead(recorded).Packet is PrUserLoginSucceeded login:
                    foreach (var catalog in login.Catalogs)
                    {
                        LearnGenders(catalog);
                    }

                    break;

                // The reply to a room's creator carries the difficulty its room starts with.
                case var id when id == PcGameSessionInit.RegistryId
                                 && RegistryIdOf(request) == PqGameSessionCreate.RegistryId
                                 && PcGameSessionInit.TryRead(recorded).Packet is { Room.GameType: GameType.Mission } reply:
                    _missionDifficulties[reply.Room.RoomNumber] = reply.Difficulty;
                    break;

                case var id when id == PcGameSessionStartMissionLevel.RegistryId:
                    _levelsBegun.Add(PcGameSessionStartMissionLevel.TryRead(recorded).Packet!.Level);
                    break;
            }
        }

        /// <summary><paramref name="recorded"/>, with the adjustments the remarks above describe.</summary>
        public byte[] Adjusted(byte[] recorded)
        {
            var adjusted = new ArrayBufferWriter<byte>();
            switch (RegistryIdOf(recorded))
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

                case var id when id == PcGameSessionInit.RegistryId
                                 && PcGameSessionInit.TryRead(recorded).Packet is { } reply:
                    var difficulty = reply.Room.GameType == GameType.Mission
                                     && _missionDifficulties.TryGetValue(reply.Room.RoomNumber, out var missionDifficulty)
                        ? missionDifficulty
                        : reply.Difficulty;
                    new PcGameSessionInit(
                        reply.Room,
                        reply.Option,
                        [.. reply.Slots.Select(WithGender)],
                        difficulty,
                        TeamOneName: string.Empty,
                        TeamTwoName: string.Empty).Write(adjusted);
                    break;

                case var id when id == PcGameSessionUpdateSlot.RegistryId
                                 && PcGameSessionUpdateSlot.TryRead(recorded).Packet is { } update:
                    new PcGameSessionUpdateSlot(update.Change, WithGender(update.Slot), update.TeamName).Write(adjusted);
                    break;

                // Only a mission's start is compared: a start between teams ends the replay first.
                case var id when id == PcGameSessionStartGame.RegistryId
                                 && PcGameSessionStartGame.TryRead(recorded).Packet is { } start:
                    new PcGameSessionStartGame(
                        start.MapId,
                        [.. start.Players.Where(player => Seating.TeamOf(player.SlotIndex) == 1)],
                        start.Wind,
                        start.Field24,
                        start.CaptainSlots,
                        start.MissionSeed).Write(adjusted);
                    break;

                default:
                    return recorded;
            }

            return adjusted.WrittenSpan.ToArray();
        }

        private void LearnGenders(CharacterCatalog catalog)
        {
            foreach (var character in catalog.Characters)
            {
                _genders[character.CharacterId] = character.Gender;
            }
        }

        // The slot, its character's appearance carrying the character's gender.
        private GameSlotInfo WithGender(GameSlotInfo slot)
        {
            if (slot.Occupant is not { } occupant || !_genders.TryGetValue(occupant.CharacterId, out var gender))
            {
                return slot;
            }

            return new GameSlotInfo(
                slot.SlotIndex,
                slot.Status,
                new SlotOccupant(
                    occupant.PlayerId,
                    occupant.CharacterId,
                    occupant.Name,
                    occupant.GuildName,
                    occupant.Level,
                    occupant.Team,
                    occupant.Appearance with { Gender = gender },
                    occupant.WeaponId));
        }
    }

    /// <summary>Chance as the recordings have it: a match's seed of 0, and the first of the result cards left.</summary>
    private sealed class RecordedChance : Random
    {
        public override long NextInt64(long minValue, long maxValue) => minValue;

        public override int Next(int maxValue) => 0;
    }
}
