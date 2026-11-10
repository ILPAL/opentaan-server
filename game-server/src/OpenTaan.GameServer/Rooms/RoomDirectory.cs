using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Lobby;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;
using OpenTaan.Transport;

namespace OpenTaan.GameServer.Rooms;

/// <summary>Whether a room was created, and why not.</summary>
public enum RoomCreation
{
    /// <summary>The room exists, with the connection as its host.</summary>
    Created,

    /// <summary>The connection is in a room already.</summary>
    AlreadyInRoom,

    /// <summary>The channel holds as many rooms as it may.</summary>
    TooManyRooms,

    /// <summary>Every room number is in use.</summary>
    NoNumberLeft,
}

/// <summary>Every room, and what the people in a room and in a lobby are told as rooms change. Shared by every connection.</summary>
/// <remarks>
/// <para>
/// One lock covers every room. Changing a room, telling everyone in it, and telling the
/// lobby of its channel are one step under that lock, so everyone in a room is told of the
/// same changes in the same order, and two players racing for a room's last slot cannot
/// both take it. A player entering a lobby is listed its rooms and joins it in one step
/// under the same lock, so a room created or changed at that moment reaches it exactly
/// once: in the list, or as a change after it.
/// </para>
/// <para>
/// The lobby's own lock is taken under this one, never the other way round. Sending only
/// queues a packet, so nothing under the lock waits on a network.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory(LobbyRoster lobbies, IOptions<RoomOptions> options, ILogger<RoomDirectory> logger)
{
    // Why a request to act in a room is dropped when the connection is in none that waits.
    private const string NotWaiting = "it is in no room waiting for a match";

    private static readonly int EmptyListSize = SizeOf(new PcLobbyInitRoomList([]));
    private static readonly int EmptyUpdateSize = SizeOf(new PcLobbyUpdateRoomList([]));

    private readonly Dictionary<ushort, Room> _rooms = [];
    private readonly Dictionary<ClientSession, Room> _roomOf = [];
    private readonly RoomNumbers _numbers = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates a room with the rules <paramref name="requested"/> asks for, which
    /// <see cref="RoomRules"/> must allow, and puts <paramref name="host"/> in it as its host.
    /// </summary>
    /// <remarks>
    /// The host's characters take the first slots of the first team. On success the host
    /// is taken off its lobby's player list, the lobby is told of the room, and the host
    /// is sent it.
    /// </remarks>
    public RoomCreation Create(RoomMember host, GameSessionInfo requested)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(requested);

        lock (_lock)
        {
            if (_roomOf.ContainsKey(host.Session))
            {
                return RoomCreation.AlreadyInRoom;
            }

            if (_rooms.Values.Count(room => room.ChannelId == host.ChannelId) >= options.Value.MaxPerChannel)
            {
                return RoomCreation.TooManyRooms;
            }

            if (_numbers.Take(_rooms.ContainsKey) is not { } number)
            {
                return RoomCreation.NoNumberLeft;
            }

            var room = new Room(number, requested, host);

            // An empty room always has the first team's slots for a connection's one or two characters.
            if (Seating.Choose(room.Uses(), host.Characters.Count, out var slots) is { } refusal)
            {
                throw new InvalidOperationException($"A new room refused its host's characters: {refusal}.");
            }

            _rooms.Add(number, room);
            SeatLocked(room, host, slots);
            LogCreated(logger, number, room.ChannelId, host.Session.Id, room.GameType);
            return RoomCreation.Created;
        }
    }

    /// <summary>Puts <paramref name="member"/> in room <paramref name="roomNumber"/>, or says why not.</summary>
    /// <remarks>
    /// A room in another channel is not one the member can see, so it is no such room. On
    /// success the member is taken off its lobby's player list, everyone already in the
    /// room is told of each slot it takes, the lobby of the channel is told the room's new
    /// count, and the member is sent the room.
    /// </remarks>
    /// <returns>Null once the member is in the room; otherwise why it is not.</returns>
    public EnterRefusal? Enter(RoomMember member, ushort roomNumber, string password)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(password);

        lock (_lock)
        {
            if (_roomOf.ContainsKey(member.Session))
            {
                return EnterRefusal.AlreadyInRoom;
            }

            if (!_rooms.TryGetValue(roomNumber, out var room) || room.ChannelId != member.ChannelId)
            {
                return EnterRefusal.NoSuchRoom;
            }

            if (room.Status != RoomStatus.Waiting)
            {
                return EnterRefusal.AlreadyStarted;
            }

            if (room.Password is not null && !IsPassword(room.Password, password))
            {
                return EnterRefusal.WrongPassword;
            }

            if (Seating.Choose(room.Uses(), member.Characters.Count, out var slots) is { } refusal)
            {
                return refusal;
            }

            SeatLocked(room, member, slots);
            return null;
        }
    }

    /// <summary>Puts <paramref name="member"/> in whichever room suits it best, or says why none.</summary>
    /// <remarks>
    /// Only a waiting room in the member's channel with no password, that plays no mission
    /// and can seat all of the member's characters on one team is chosen: the fullest, then
    /// the one with the lowest number. A request names no password, so a room with one is
    /// never chosen; and dropping a stranger into someone's mission is not what a quick
    /// join is for. The choice and the seating are one step under the lock, so the room
    /// chosen cannot fill before the member sits.
    /// </remarks>
    /// <returns>Null once the member is in a room; otherwise why it is not.</returns>
    public EnterRefusal? QuickJoin(RoomMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        lock (_lock)
        {
            if (_roomOf.ContainsKey(member.Session))
            {
                return EnterRefusal.AlreadyInRoom;
            }

            var candidates = _rooms.Values
                .Where(room => room.ChannelId == member.ChannelId
                               && room.Status == RoomStatus.Waiting
                               && room.Password is null
                               && !room.IsMission)
                .OrderByDescending(room => room.SeatedCount)
                .ThenBy(room => room.Number);

            foreach (var room in candidates)
            {
                if (Seating.Choose(room.Uses(), member.Characters.Count, out var slots) is null)
                {
                    SeatLocked(room, member, slots);
                    return null;
                }
            }

            return EnterRefusal.RoomFull;
        }
    }

    /// <summary>
    /// Takes <paramref name="session"/> out of its room, if it is in one, and tells
    /// everyone left in it and the lobby of its channel.
    /// </summary>
    /// <remarks>
    /// Everyone left in the room is told of each slot freed. When the host leaves, the
    /// connection in the lowest slot becomes host, and a host is never ready: the room is
    /// told that it is not ready first, and only then who hosts it, so no client shows the
    /// host's buttons on a player it still shows as ready. A room left empty is closed and
    /// taken off the lobby's list.
    /// </remarks>
    /// <returns>Whether the connection was in a room.</returns>
    public bool Leave(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            return LeaveLocked(session);
        }
    }

    /// <summary>
    /// Makes a guest's characters ready to start, or no longer ready, and tells everyone in
    /// its room, the guest included.
    /// </summary>
    /// <remarks>
    /// All of the connection's characters change together, as the client shows them. The
    /// host has no Ready button, since it starts the match, so a request from it is dropped.
    /// The slots keep the change, so a player entering later is shown it.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? Ready(ClientSession session, bool ready)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (room.IsHost(session))
            {
                return "the host starts the match instead";
            }

            room.SetReady(session, ready);
            SendToRoomLocked(room, new PcGameSessionReady(room.MemberOf(session)!.PlayerId, ready));
            return null;
        }
    }

    /// <summary>
    /// Moves a player's characters to the other team, together, into its lowest open slots,
    /// and tells everyone in the room, the player included.
    /// </summary>
    /// <remarks>
    /// The players of a mission room are all on one side, and a ready player stays where it
    /// is: the client asks for neither. The other team needs an open slot for each of the
    /// player's characters, so that a connection's characters are never split, which the
    /// client could not show. The characters keep their weapons.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? ChangeTeam(ClientSession session, byte team)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (room.IsMission)
            {
                return "a mission room's players are all on one side";
            }

            if (room.IsReady(session))
            {
                return "a ready player stays on its team";
            }

            var from = room.SlotsOf(session);
            if (team is not (1 or 2) || team == Seating.TeamOf(from[0]))
            {
                return $"team {team} is not the other team";
            }

            var open = room.OpenSlotsOn(team);
            if (open.Count < from.Count)
            {
                return $"team {team} has {open.Count} open slots for {from.Count} characters";
            }

            IReadOnlyList<byte> to = [.. open.Take(from.Count)];
            room.Move(from, to);
            SendToRoomLocked(room, PcGameSessionChangeTeam.Moving(room.MemberOf(session)!.PlayerId, team, from, to));
            return null;
        }
    }

    /// <summary>
    /// Gives one of a player's characters the weapon the player picked for it, and tells
    /// everyone in the room, the player included.
    /// </summary>
    /// <remarks>
    /// The weapon is kept as it is: the weapons the client offers come from its own files,
    /// which this server does not read, and its random pick is decided when the match
    /// starts. The slot keeps the weapon, so a player entering later is shown it. Only the
    /// player's own characters are its to arm.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? ChangeWeapon(ClientSession session, uint characterId, ushort weaponId)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (room.SlotOf(characterId) is not { } slot || room.MemberIn(slot).Session != session)
            {
                return $"character {characterId} is not one of the player's own in its room";
            }

            room.Arm(slot, weaponId);
            SendToRoomLocked(room, new PcGameSessionChangeWeapon(characterId, weaponId));
            return null;
        }
    }

    /// <summary>
    /// Gives a room the options its host asks for, when <see cref="RoomRules.OptionsProblem"/>
    /// allows them, and answers the host either way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host's options dialog stays locked until it hears back, so the host is always
    /// answered: with the new options, which everyone in the room is sent and the lobby
    /// told, or with the room's options as they are, sent to the host alone. A request from
    /// anyone else is dropped, since only the host has the dialog.
    /// </para>
    /// <para>
    /// The difficulty is kept only in a mission room. Elsewhere it means nothing, and the
    /// room is sent back what the request carried.
    /// </para>
    /// </remarks>
    /// <param name="session">The connection asking.</param>
    /// <param name="requested">The options asked for.</param>
    /// <param name="maps">The maps a host may pick in a room of teams in the room's channel.</param>
    /// <returns>Null once done; otherwise why the request was refused or dropped, for the log.</returns>
    public string? ChangeOptions(ClientSession session, PqGameSessionChangeGameOption requested, IReadOnlyCollection<ushort> maps)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(maps);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (!room.IsHost(session))
            {
                return "only the host changes the room's options";
            }

            if (RoomRules.OptionsProblem(room.GameType, requested, maps) is { } problem)
            {
                session.Send(room.Options(room.Difficulty));
                return problem;
            }

            room.SetOptions(requested.GameMode, requested.ChanceMode, requested.MapId, requested.Difficulty);
            SendToRoomLocked(room, room.Options(room.IsMission ? room.Difficulty : requested.Difficulty));
            lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
            return null;
        }
    }

    /// <summary>
    /// Opens an empty slot of a room to players entering, or closes it, as its host asks,
    /// and tells everyone in the room and the lobby, whose maximum for the room changes.
    /// </summary>
    /// <remarks>
    /// Only an empty slot changes: the client sets a slot's status without checking what it
    /// holds, so closing a taken one would wipe its character off every screen. The mission's
    /// own side of a mission room stays closed, and a request that would change nothing is
    /// dropped, as is one from anyone but the host.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? OpenOrCloseSlot(ClientSession session, byte slot, bool open)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (!room.IsHost(session))
            {
                return "only the host opens and closes slots";
            }

            if (slot >= GameSlotInfo.SlotCount)
            {
                return $"slot {slot} is not one of the room's";
            }

            if (room.IsMissionSide(slot))
            {
                return $"slot {slot} is on the mission's own side";
            }

            var use = room.Uses()[slot];
            if (use == SlotUse.Taken)
            {
                return $"slot {slot} holds a character";
            }

            if (use == (open ? SlotUse.Open : SlotUse.Closed))
            {
                return $"slot {slot} is {use} already";
            }

            room.SetClosed(slot, closed: !open);
            SendToRoomLocked(room, new PcGameSessionOpenCloseSlot(open, slot));
            lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
            return null;
        }
    }

    /// <summary>
    /// Sends a player out of a room, as its host asks: everyone in the room is told, that
    /// player included, and the player then leaves the room as it would on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host names a character, and the room is told whose it is: the client finds its own
    /// slots by the player's id. All of that player's characters go. Its client asks to leave
    /// as soon as it is told, and that request, which then finds it in no room, is answered
    /// as left.
    /// </para>
    /// <para>
    /// A character no slot holds is dropped, as is one of the host's own: the host leaves by
    /// itself. Nothing stops the player coming back.
    /// </para>
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? Expel(ClientSession session, uint characterId)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (!room.IsHost(session))
            {
                return "only the host sends players out";
            }

            if (room.SlotOf(characterId) is not { } slot)
            {
                return $"no slot holds character {characterId}";
            }

            var target = room.MemberIn(slot);
            if (target.Session == session)
            {
                return $"character {characterId} is the host's own";
            }

            SendToRoomLocked(room, new PcGameSessionUserExpel(target.PlayerId));
            LeaveLocked(target.Session);
            LogExpelled(logger, room.Number, session.Id, target.Session.Id);
            return null;
        }
    }

    /// <summary>The characters in room <paramref name="roomNumber"/>, or null when the channel has no such room.</summary>
    public IReadOnlyList<UserSimpleInfo>? PlayersIn(ushort channelId, ushort roomNumber)
    {
        lock (_lock)
        {
            return _rooms.TryGetValue(roomNumber, out var room) && room.ChannelId == channelId ? room.Players() : null;
        }
    }

    /// <summary>The number of the room <paramref name="session"/> is in, or null.</summary>
    public ushort? RoomOf(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            return _roomOf.TryGetValue(session, out var room) ? room.Number : null;
        }
    }

    /// <summary>
    /// Sends <paramref name="session"/> the rooms in the lobby of
    /// <paramref name="channelId"/>, then puts it in that lobby with
    /// <paramref name="characters"/>, which sends it the lobby's players.
    /// </summary>
    public void EnterLobby(ClientSession session, ushort channelId, IReadOnlyList<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(characters);

        lock (_lock)
        {
            var listings = _rooms.Values
                .Where(room => room.ChannelId == channelId)
                .OrderBy(room => room.Number)
                .Select(room => room.Listing())
                .ToArray();

            foreach (var packet in ListOf(listings))
            {
                session.Send(packet);
            }

            lobbies.Enter(session, channelId, characters);
        }
    }

    /// <summary>
    /// The room list a player entering a lobby is sent: one list, and when that would not
    /// fit a frame, as many changes after it as the rest needs, each adding rooms.
    /// </summary>
    public static IReadOnlyList<IPacket> ListOf(IReadOnlyList<GameSessionInfoPack> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);

        var packets = new List<IPacket>();
        var start = 0;
        do
        {
            var budget = TransportLimits.MaxEncryptedBody - (start == 0 ? EmptyListSize : EmptyUpdateSize);
            var end = start;
            while (end < listings.Count && (budget -= SizeOf(listings[end])) >= 0)
            {
                end++;
            }

            // An entry too large for a frame of its own still goes, and the send refuses it.
            end = Math.Max(end, Math.Min(start + 1, listings.Count));

            var slice = listings.Skip(start).Take(end - start).ToArray();
            packets.Add(start == 0 ? new PcLobbyInitRoomList(slice) : new PcLobbyUpdateRoomList(slice));
            start = end;
        }
        while (start < listings.Count);

        return packets;
    }

    // Takes the connection out of its room, if it is in one, and tells everyone left in it
    // and the lobby; see Leave.
    private bool LeaveLocked(ClientSession session)
    {
        if (!_roomOf.Remove(session, out var room))
        {
            return false;
        }

        var wasHost = room.IsHost(session);
        var freed = room.Vacate(session);

        if (room.IsEmpty)
        {
            _rooms.Remove(room.Number);
            lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([GameSessionInfoPack.Removal(room.Number)]));
            LogClosed(logger, room.Number, room.ChannelId);
            return true;
        }

        foreach (var slot in freed)
        {
            SendToRoomLocked(room, new PcGameSessionUpdateSlot(SlotChange.Vacated, room.SlotInfo(slot), TeamName: string.Empty));
        }

        if (wasHost)
        {
            var host = room.HandOver();
            SendToRoomLocked(room, new PcGameSessionReady(host.PlayerId, IsReady: false));
            SendToRoomLocked(room, new PcGameSessionUpdateOption(room.Option()));
            LogHandedOver(logger, room.Number, host.Session.Id);
        }

        lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
        return true;
    }

    // The room the connection is in, while it waits for a match: the only time a player
    // acts on it from inside.
    private Room? WaitingRoomLocked(ClientSession session) =>
        _roomOf.TryGetValue(session, out var room) && room.Status == RoomStatus.Waiting ? room : null;

    // Seats the member, takes it off its lobby's player list, tells everyone else in the
    // room and then the lobby, and sends the member the room. The member leaves the lobby
    // before the lobby is told, so it is not sent the change that puts it in the room. It
    // is sent the room last, so by the time it has its answer everyone else has been told.
    private void SeatLocked(Room room, RoomMember member, IReadOnlyList<byte> slots)
    {
        room.Seat(member, slots);
        _roomOf[member.Session] = room;
        lobbies.Leave(member.Session);

        foreach (var slot in slots)
        {
            SendToRoomLocked(room, new PcGameSessionUpdateSlot(SlotChange.Seated, room.SlotInfo(slot), TeamName: string.Empty), except: member.Session);
        }

        lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
        member.Session.Send(room.Reply(member));
    }

    // A copy of the members, so the room may change while it is being told: a send that
    // finds a connection's queue full closes that connection.
    private static void SendToRoomLocked(Room room, IPacket packet, ClientSession? except = null)
    {
        foreach (var member in room.Members())
        {
            if (member.Session != except)
            {
                member.Session.Send(packet);
            }
        }
    }

    // Compared in time that does not depend on where the two first differ.
    private static bool IsPassword(string password, string presented) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(presented));

    // What an entry adds to a list.
    private static int SizeOf(GameSessionInfoPack listing) => SizeOf(new PcLobbyUpdateRoomList([listing])) - EmptyUpdateSize;

    private static int SizeOf(IPacket packet)
    {
        var body = new ArrayBufferWriter<byte>();
        packet.Write(body);
        return body.WrittenCount;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Room {RoomNumber} created in channel {ChannelId} by connection {ConnectionId}, playing {GameType}")]
    private static partial void LogCreated(ILogger logger, ushort roomNumber, ushort channelId, long connectionId, GameType gameType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber} in channel {ChannelId} closed: its last player left")]
    private static partial void LogClosed(ILogger logger, ushort roomNumber, ushort channelId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber} is now hosted by connection {ConnectionId}")]
    private static partial void LogHandedOver(ILogger logger, ushort roomNumber, long connectionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Room {RoomNumber}: its host, connection {HostConnectionId}, sent connection {ConnectionId} out")]
    private static partial void LogExpelled(ILogger logger, ushort roomNumber, long hostConnectionId, long connectionId);
}
