using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>A room: its rules, its slots, and who hosts it. Changed only under <see cref="RoomDirectory"/>'s lock.</summary>
/// <remarks>
/// <para>
/// A room has <see cref="GameSlotInfo.SlotCount"/> slots, on two teams as
/// <see cref="Seating"/> describes. In a mission room the second team's slots are closed
/// for good: that side of a mission is played by the server.
/// </para>
/// <para>
/// What the room holds and what its members and the lobby are sent come from the same
/// place, here, so the two cannot drift apart.
/// </para>
/// </remarks>
internal sealed class Room
{
    // The map a new room has: the first map in a room of teams, and 0 in a mission room,
    // as the reference sessions have them. The create dialog sends 0 for both.
    private const ushort FirstMap = 1;
    private const ushort MissionMap = 0;

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
        MapId = IsMission ? MissionMap : FirstMap;
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
    public GameMode GameMode { get; }

    /// <summary>The chances its match gives.</summary>
    public ChanceMode ChanceMode { get; }

    /// <summary>The map its match is played on.</summary>
    public ushort MapId { get; }

    /// <summary>A mission's difficulty: 0, the easiest, until the host changes it.</summary>
    public byte Difficulty { get; }

    /// <summary>Where the room stands: waiting, until matches are played.</summary>
    public RoomStatus Status => RoomStatus.Waiting;

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

    // The character in a slot. Its weapon is the default one until the player picks
    // another for the match.
    private static SlotOccupant Occupant(int slot, TakenSlot seat) => new(
        seat.Member.PlayerId,
        seat.Character.CharacterId,
        seat.Character.Name,
        seat.Character.Info.GuildName,
        seat.Character.Info.Level,
        Seating.TeamOf(slot),
        seat.Character.Appearance,
        WeaponId: 0);

    private sealed class TakenSlot(RoomMember member, Character character)
    {
        public RoomMember Member { get; } = member;

        public Character Character { get; } = character;

        public bool IsReady { get; set; }
    }
}
