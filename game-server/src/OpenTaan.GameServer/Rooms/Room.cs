using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>A room: its rules, its slots, who hosts it, and the match it plays. Changed only under <see cref="RoomDirectory"/>'s lock.</summary>
/// <remarks>
/// <para>
/// A room has <see cref="GameSlotInfo.SlotCount"/> slots, on two teams as
/// <see cref="Seating"/> describes. In a mission room the second team's slots are closed
/// for good: that side is the mission's own enemies, which every client plays from its own
/// copy of the mission.
/// </para>
/// <para>
/// What the room holds and what its members and the lobby are sent come from the same
/// place, here, so the two cannot drift apart.
/// </para>
/// </remarks>
internal sealed class Room
{
    /// <summary>The mission a new mission room plays until its host picks another, as the reference sessions have it.</summary>
    public const ushort FirstMission = 0;

    // The map a new room of teams has, as the reference sessions have it. The create
    // dialog sends 0 for both kinds of room.
    private const ushort FirstMap = 1;

    private readonly TakenSlot?[] _seats = new TakenSlot?[GameSlotInfo.SlotCount];
    private readonly bool[] _closed = new bool[GameSlotInfo.SlotCount];

    /// <summary>
    /// A room numbered <paramref name="number"/> in <paramref name="host"/>'s channel, with
    /// the rules <paramref name="requested"/> asks for, and nobody in it yet.
    /// </summary>
    /// <remarks>
    /// Only the rules are taken from the request. The status, the player counts and the
    /// map are the server's, and the field the create dialog leaves at 0 stays 0.
    /// </remarks>
    public Room(ushort number, GameSessionInfo requested, RoomMember host)
    {
        Number = number;
        ChannelId = host.ChannelId;
        Name = requested.Name;
        Password = requested.Password;
        GameType = requested.GameType;
        GameMode = requested.GameMode;
        ChanceMode = requested.ChanceMode;
        MapId = IsMission ? FirstMission : FirstMap;
        Host = host;

        for (var slot = 0; slot < _closed.Length; slot++)
        {
            _closed[slot] = IsMission && Seating.TeamOf(slot) == 2;
        }
    }

    /// <summary>The room's number, by which every packet names it.</summary>
    public ushort Number { get; }

    /// <summary>The channel whose lobby lists the room.</summary>
    public ushort ChannelId { get; }

    /// <summary>The room's name.</summary>
    public string Name { get; }

    /// <summary>The password a player must give to enter, or null.</summary>
    public string? Password { get; }

    /// <summary>The kind of match the room plays.</summary>
    public GameType GameType { get; }

    /// <summary>The rules its match is played by.</summary>
    public GameMode GameMode { get; private set; }

    /// <summary>The chances its match gives.</summary>
    public ChanceMode ChanceMode { get; private set; }

    /// <summary>
    /// The map its match is played on, or a mission's own number in a mission room. A
    /// random pick is kept as it is, so the room shows it; the match decides the map.
    /// </summary>
    public ushort MapId { get; private set; }

    /// <summary>A mission's difficulty: 0, the easiest, until the host changes it. Always 0 outside a mission room.</summary>
    public byte Difficulty { get; private set; }

    /// <summary>Where the room stands: waiting for players, or playing a match, its result screen included.</summary>
    public RoomStatus Status { get; private set; } = RoomStatus.Waiting;

    /// <summary>The match the room plays, from its start until the room waits again, or null.</summary>
    public Match? Match { get; private set; }

    /// <summary>The connection hosting the room.</summary>
    public RoomMember Host { get; private set; }

    /// <summary>Whether the room plays missions.</summary>
    public bool IsMission => GameType == GameType.Mission;

    /// <summary>Whether no slot holds a character.</summary>
    public bool IsEmpty => SeatedCount == 0;

    /// <summary>How many characters the room holds.</summary>
    public int SeatedCount => _seats.Count(seat => seat is not null);

    /// <summary>How many slots are not closed, taken or not: as many characters as the room can hold now.</summary>
    public int OpenCount => _closed.Count(closed => !closed);

    /// <summary>The connections in the room, each once, in the order of the first slot each holds.</summary>
    public IReadOnlyList<RoomMember> Members() =>
        [.. _seats.OfType<TakenSlot>().Select(seat => seat.Member).DistinctBy(member => member.Session)];

    /// <summary>What each slot holds, by index.</summary>
    public IReadOnlyList<SlotUse> Uses() => [.. Enumerable.Range(0, _seats.Length).Select(UseOf)];

    /// <summary>Whether <paramref name="session"/> hosts the room.</summary>
    public bool IsHost(ClientSession session) => Host.Session == session;

    /// <summary>The connection <paramref name="session"/> as the room holds it, or null when it is not in the room.</summary>
    public RoomMember? MemberOf(ClientSession session) =>
        _seats.OfType<TakenSlot>().FirstOrDefault(seat => seat.Member.Session == session)?.Member;

    /// <summary>The slots <paramref name="session"/> holds, in rising order: none when it is not in the room.</summary>
    public IReadOnlyList<byte> SlotsOf(ClientSession session) =>
        [.. Enumerable.Range(0, _seats.Length).Where(slot => _seats[slot]?.Member.Session == session).Select(slot => (byte)slot)];

    /// <summary>The open slots of team <paramref name="team"/>, in rising order.</summary>
    public IReadOnlyList<byte> OpenSlotsOn(byte team) =>
        [.. Enumerable.Range(0, _seats.Length).Where(slot => Seating.TeamOf(slot) == team && UseOf(slot) == SlotUse.Open).Select(slot => (byte)slot)];

    /// <summary>The slot holding character <paramref name="characterId"/>, or null when no slot does.</summary>
    public byte? SlotOf(uint characterId) =>
        Array.FindIndex(_seats, seat => seat?.Character.CharacterId == characterId) is var slot and >= 0 ? (byte)slot : null;

    /// <summary>The connection whose character is in slot <paramref name="slot"/>, which must hold one.</summary>
    public RoomMember MemberIn(byte slot) =>
        _seats[slot]?.Member ?? throw new InvalidOperationException($"Slot {slot} holds no character.");

    /// <summary>Whether every character in the room is ready to start, its host's apart: the host starts the match instead.</summary>
    public bool GuestsReady => _seats.OfType<TakenSlot>().All(seat => seat.Member.Session == Host.Session || seat.IsReady);

    /// <summary>The connection playing the character in each slot, by slot: the slots holding characters only.</summary>
    public IReadOnlyDictionary<byte, ClientSession> PlayersBySlot() => Enumerable.Range(0, _seats.Length)
        .Where(slot => _seats[slot] is not null)
        .ToDictionary(slot => (byte)slot, slot => _seats[slot]!.Member.Session);

    /// <summary>How each character in the room starts a match, in the order of their slots.</summary>
    public IReadOnlyList<GameSlotPlayInfo> PlayData() =>
    [
        .. Enumerable.Range(0, _seats.Length)
            .Where(slot => _seats[slot] is not null)
            .Select(slot => MatchRules.StartOf((byte)slot, _seats[slot]!.WeaponId)),
    ];

    /// <summary>The level of the character in each slot, null for a slot with none, by slot.</summary>
    public IReadOnlyList<ushort?> Levels() => [.. _seats.Select(seat => seat?.Character.Info.Level)];

    /// <summary>The character in each slot, null for a slot with none, by slot.</summary>
    public IReadOnlyList<uint?> CharacterIds() => [.. _seats.Select(seat => seat?.Character.CharacterId)];

    /// <summary>The slots holding characters, in rising order.</summary>
    public IReadOnlyList<byte> TakenSlots() => [.. Enumerable.Range(0, _seats.Length).Where(slot => _seats[slot] is not null).Select(slot => (byte)slot)];

    /// <summary>Starts <paramref name="match"/>: the room plays it until <see cref="EndMatch"/>.</summary>
    public void BeginMatch(Match match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        Status = RoomStatus.Playing;
    }

    /// <summary>Lets go of the match: the room waits for players again.</summary>
    public void EndMatch()
    {
        Match?.Dispose();
        Match = null;
        Status = RoomStatus.Waiting;
    }

    /// <summary>Makes no character ready, as the client does itself when a match ends.</summary>
    public void ClearReady()
    {
        foreach (var seat in _seats.OfType<TakenSlot>())
        {
            seat.IsReady = false;
        }
    }

    /// <summary>Whether <paramref name="session"/>'s characters are ready to start. They always are together.</summary>
    public bool IsReady(ClientSession session) => _seats.Any(seat => seat?.Member.Session == session && seat.IsReady);

    /// <summary>Makes every character <paramref name="session"/> plays ready to start, or not.</summary>
    public void SetReady(ClientSession session, bool ready)
    {
        foreach (var seat in _seats.OfType<TakenSlot>().Where(seat => seat.Member.Session == session))
        {
            seat.IsReady = ready;
        }
    }

    /// <summary>
    /// Moves the character in each slot of <paramref name="from"/> to the slot at the same
    /// place in <paramref name="to"/>. The characters keep their weapons and whether they
    /// are ready.
    /// </summary>
    public void Move(IReadOnlyList<byte> from, IReadOnlyList<byte> to)
    {
        if (from.Count != to.Count || from.Any(slot => UseOf(slot) != SlotUse.Taken) || to.Any(slot => UseOf(slot) != SlotUse.Open))
        {
            throw new InvalidOperationException("Each character moves from its slot to an open one.");
        }

        var moving = from.Select(slot => _seats[slot]!).ToArray();
        foreach (var slot in from)
        {
            _seats[slot] = null;
        }

        for (var position = 0; position < to.Count; position++)
        {
            _seats[to[position]] = moving[position];
        }
    }

    /// <summary>Gives the character in slot <paramref name="slot"/>, which must hold one, weapon <paramref name="weaponId"/> for the match.</summary>
    public void Arm(byte slot, ushort weaponId) =>
        (_seats[slot] ?? throw new InvalidOperationException($"Slot {slot} holds no character.")).WeaponId = weaponId;

    /// <summary>Whether slot <paramref name="slot"/> belongs to the mission's own side, closed for good.</summary>
    public bool IsMissionSide(int slot) => IsMission && Seating.TeamOf(slot) == 2;

    /// <summary>Closes slot <paramref name="slot"/> to players entering, or opens it. It must hold no character.</summary>
    public void SetClosed(byte slot, bool closed)
    {
        if (_seats[slot] is not null || IsMissionSide(slot))
        {
            throw new InvalidOperationException($"Slot {slot} holds a character, or the mission's side.");
        }

        _closed[slot] = closed;
    }

    /// <summary>Sets the rules the room's match is played by, its chances, its map, and a mission's difficulty.</summary>
    public void SetOptions(GameMode gameMode, ChanceMode chanceMode, ushort mapId, byte difficulty)
    {
        GameMode = gameMode;
        ChanceMode = chanceMode;
        MapId = mapId;
        Difficulty = IsMission ? difficulty : (byte)0;
    }

    /// <summary>The room's options as everyone in it is told them, with <paramref name="difficulty"/> as the difficulty.</summary>
    public PcGameSessionChangeGameOption Options(byte difficulty) => new(MapId, GameMode, ChanceMode, difficulty);

    /// <summary>Seats <paramref name="member"/>'s characters, in order, in <paramref name="slots"/>.</summary>
    public void Seat(RoomMember member, IReadOnlyList<byte> slots)
    {
        if (slots.Count != member.Characters.Count || slots.Any(slot => UseOf(slot) != SlotUse.Open))
        {
            throw new InvalidOperationException("A connection's characters each take one open slot.");
        }

        for (var position = 0; position < slots.Count; position++)
        {
            _seats[slots[position]] = new TakenSlot(member, member.Characters[position]);
        }
    }

    /// <summary>Empties every slot <paramref name="session"/> holds, and returns them in rising order.</summary>
    public IReadOnlyList<byte> Vacate(ClientSession session)
    {
        var freed = new List<byte>();
        for (var slot = 0; slot < _seats.Length; slot++)
        {
            if (_seats[slot]?.Member.Session == session)
            {
                _seats[slot] = null;
                freed.Add((byte)slot);
            }
        }

        return freed;
    }

    /// <summary>
    /// Makes the connection in the lowest slot the host, with none of its characters
    /// ready, and returns it. The room must hold someone.
    /// </summary>
    public RoomMember HandOver()
    {
        var host = _seats.OfType<TakenSlot>().First().Member;
        foreach (var seat in _seats.OfType<TakenSlot>().Where(seat => seat.Member.Session == host.Session))
        {
            // A host does not ready: it starts the match.
            seat.IsReady = false;
        }

        Host = host;
        return host;
    }

    /// <summary>The room as its members are sent it.</summary>
    public GameSessionInfo Record() => Info(maxPlayers: GameSlotInfo.SlotCount, Password);

    /// <summary>The room as the lobby lists it.</summary>
    /// <remarks>
    /// The lobby is told whether the room has a password, never what it is: the client asks
    /// a player entering for it and sends what was typed, without comparing it to anything.
    /// It lets a player try to enter while the room holds fewer characters than the listed
    /// maximum, so that is the number of slots not closed.
    /// </remarks>
    public GameSessionInfoPack Listing() =>
        GameSessionInfoPack.Listing(Info(maxPlayers: OpenCount, Password is null ? null : string.Empty));

    /// <summary>Slot <paramref name="slot"/> as the client is sent it.</summary>
    public GameSlotInfo SlotInfo(int slot) => _seats[slot] is { } seat
        ? GameSlotInfo.Taken((byte)slot, Occupant(slot, seat), seat.IsReady)
        : _closed[slot] ? GameSlotInfo.Closed((byte)slot) : GameSlotInfo.Open((byte)slot);

    /// <summary>Who hosts the room, as the client is told it.</summary>
    /// <remarks>
    /// The host's player id in both fields, as the reference sessions carry it; see
    /// <see cref="GameSessionOption"/>.
    /// </remarks>
    public GameSessionOption Option() => new(Host.PlayerId, Host.PlayerId);

    /// <summary>The answer that puts <paramref name="member"/>, already seated, in the room.</summary>
    /// <remarks>
    /// Its one byte the client reads only in a mission room, as the mission's difficulty,
    /// carries the room's difficulty there, so everyone in the room is shown the same.
    /// Elsewhere it carries the member's first slot, as the reference sessions do. The two
    /// team names are read only in arena channels, and are empty.
    /// </remarks>
    public PcGameSessionInit Reply(RoomMember member) => new(
        Record(),
        Option(),
        [.. Enumerable.Range(0, _seats.Length).Select(SlotInfo)],
        IsMission ? Difficulty : FirstSlotOf(member.Session),
        TeamOneName: string.Empty,
        TeamTwoName: string.Empty);

    /// <summary>The characters in the room, in slot order, as the lobby's view of a room lists them.</summary>
    public IReadOnlyList<UserSimpleInfo> Players() =>
    [
        .. _seats.OfType<TakenSlot>().Select(seat =>
            new UserSimpleInfo(seat.Character.CharacterId, seat.Character.Name, seat.Character.Info.Level, seat.Character.Info.Gender)),
    ];

    private SlotUse UseOf(int slot) =>
        _seats[slot] is not null ? SlotUse.Taken : _closed[slot] ? SlotUse.Closed : SlotUse.Open;

    private byte FirstSlotOf(ClientSession session) =>
        (byte)Array.FindIndex(_seats, seat => seat?.Member.Session == session);

    private GameSessionInfo Info(int maxPlayers, string? password) => new(
        RoomNumber: Number,
        Status: Status,
        Grade: 0,
        Name: Name,
        GameType: GameType,
        GameMode: GameMode,
        ChanceMode: ChanceMode,
        CurrentPlayers: (byte)SeatedCount,
        MaxPlayers: (byte)maxPlayers,
        Password: password,
        MapId: MapId);

    // The character in a slot, with the weapon its player picked for the match.
    private static SlotOccupant Occupant(int slot, TakenSlot seat) => new(
        seat.Member.PlayerId,
        seat.Character.CharacterId,
        seat.Character.Name,
        seat.Character.Info.GuildName,
        seat.Character.Info.Level,
        Seating.TeamOf(slot),
        seat.Character.Appearance,
        seat.WeaponId);

    private sealed class TakenSlot(RoomMember member, Character character)
    {
        public RoomMember Member { get; } = member;

        public Character Character { get; } = character;

        public bool IsReady { get; set; }

        // The default weapon, 0, until the player picks another. Any the client offers is
        // kept as it is, its random pick included: the match decides what that becomes.
        public ushort WeaponId { get; set; }
    }
}
