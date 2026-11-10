using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Lobby;

/// <summary>One room in the lobby's room list, or its removal from it.</summary>
/// <remarks>
/// <para>
/// The list carries a room field by field: a mask names the fields present, and they
/// follow in an order of the list's own, which is not the order of a room record. The
/// client keys each entry by its room number, and adds a room it does not show yet.
/// </para>
/// <para>
/// Two kinds of entry are read and written here, which are all this server sends: a
/// <see cref="Listing"/> with every field, and a <see cref="Removal"/> with the number
/// alone. An entry with any other mask is refused as <see cref="PacketError.Unsupported"/>.
/// </para>
/// </remarks>
[WireClass("GameSessionInfoPack")]
public sealed record GameSessionInfoPack
{
    // The fields each bit of the mask stands for, and the flag that removes the room.
    private const ushort StatusField = 0x001;
    private const ushort NameField = 0x002;
    private const ushort GameModeField = 0x004;
    private const ushort CurrentPlayersField = 0x008;
    private const ushort MaxPlayersField = 0x010;
    private const ushort PasswordField = 0x020;
    private const ushort MapIdField = 0x040;
    private const ushort ChanceModeField = 0x080;
    private const ushort GameTypeField = 0x100;
    private const ushort GradeField = 0x200;
    private const ushort RemovedFlag = 0x8000;

    private const ushort EveryField = StatusField | NameField | GameModeField | CurrentPlayersField | MaxPlayersField
        | PasswordField | MapIdField | ChanceModeField | GameTypeField | GradeField;

    private GameSessionInfoPack(ushort roomNumber, GameSessionInfo? room)
    {
        RoomNumber = roomNumber;
        Room = room;
    }

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameSessionInfoPack>.RegistryId;

    /// <summary>The fewest bytes an entry occupies: its id, mask and room number.</summary>
    internal const int MinimumSize = sizeof(uint) + (2 * sizeof(ushort));

    /// <summary>The room the entry is about.</summary>
    public ushort RoomNumber { get; }

    /// <summary>The room, with every field; null for a removal.</summary>
    public GameSessionInfo? Room { get; }

    /// <summary>
    /// <paramref name="room"/>, with every field: how a room is first listed, and listed
    /// again whenever it changes.
    /// </summary>
    public static GameSessionInfoPack Listing(GameSessionInfo room)
    {
        ArgumentNullException.ThrowIfNull(room);
        return new GameSessionInfoPack(room.RoomNumber, room);
    }

    /// <summary>The room numbered <paramref name="roomNumber"/> is gone from the list.</summary>
    public static GameSessionInfoPack Removal(ushort roomNumber) => new(roomNumber, null);

    internal static GameSessionInfoPack ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var mask = reader.ReadUInt16();
        var roomNumber = reader.ReadUInt16();
        switch (mask)
        {
            case RemovedFlag:
                return Removal(roomNumber);

            case EveryField:
                var status = (RoomStatus)reader.ReadByte();
                if (status == 0)
                {
                    // A record with status 0 holds no room; see GameSessionInfo.
                    reader.Fail(PacketError.Unsupported);
                    status = RoomStatus.Waiting;
                }

                var grade = reader.ReadByte();
                var name = reader.ReadUtf16String();
                var gameType = (GameType)reader.ReadByte();
                var gameMode = (GameMode)reader.ReadUInt16();
                var chanceMode = (ChanceMode)reader.ReadByte();
                var currentPlayers = reader.ReadByte();
                var maxPlayers = reader.ReadByte();
                var mapId = reader.ReadUInt16();
                var password = reader.ReadBoolean() ? reader.ReadUtf16String() : null;
                return Listing(new GameSessionInfo(
                    roomNumber, status, grade, name, gameType, gameMode, chanceMode, currentPlayers, maxPlayers, password, mapId));

            default:
                reader.Fail(PacketError.Unsupported);
                return Removal(roomNumber);
        }
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        if (Room is null)
        {
            writer.WriteUInt16(RemovedFlag);
            writer.WriteUInt16(RoomNumber);
            return;
        }

        writer.WriteUInt16(EveryField);
        writer.WriteUInt16(RoomNumber);
        writer.WriteByte((byte)Room.Status);
        writer.WriteByte(Room.Grade);
        writer.WriteUtf16String(Room.Name);
        writer.WriteByte((byte)Room.GameType);
        writer.WriteUInt16((ushort)Room.GameMode);
        writer.WriteByte((byte)Room.ChanceMode);
        writer.WriteByte(Room.CurrentPlayers);
        writer.WriteByte(Room.MaxPlayers);
        writer.WriteUInt16(Room.MapId);
        writer.WriteBoolean(Room.Password is not null);
        if (Room.Password is not null)
        {
            writer.WriteUtf16String(Room.Password);
        }
    }
}
