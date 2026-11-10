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
            if (!_roomOf.Remove(session, out var room))
            {
                return false;
            }

            var wasHost = room.Host.Session == session;
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
}
