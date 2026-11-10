namespace OpenTaan.Protocol.Rooms;

/// <summary>A room: its number and status, its name and rules, how full it is, and its password.</summary>
/// <remarks>
/// <para>
/// The client sends one to create a room, with <see cref="RoomStatus.Requested"/>, and is
/// sent one with each room it enters. The lobby's room list carries rooms another way,
/// field by field; see <see cref="Lobby.GameSessionInfoPack"/>.
/// </para>
/// <para>
/// A record whose status is 0 carries the room number and nothing else: the client's
/// way of saying there is no room. Nothing this server reads or sends needs one, so such
/// a record is refused as <see cref="PacketError.Unsupported"/>, and cannot be built.
/// </para>
/// </remarks>
/// <param name="RoomNumber">The room's number, by which every other packet names it.</param>
/// <param name="Status">Where the room stands. Never 0.</param>
/// <param name="Grade">Left 0 by the client's create dialog. Meaning not established yet.</param>
/// <param name="Name">The room's name.</param>
/// <param name="GameType">The kind of match the room plays.</param>
/// <param name="GameMode">The rules its match is played by.</param>
/// <param name="ChanceMode">The chances its match gives.</param>
/// <param name="CurrentPlayers">How many characters are in the room.</param>
/// <param name="MaxPlayers">How many characters it holds. The client's create dialog always asks for 8.</param>
/// <param name="Password">The room's password, or null when it has none.</param>
/// <param name="MapId">The map its match is played on: a mission's own number in a mission room. The client's create dialog sends 0.</param>
[WireClass("GameSessionInfo")]
public sealed record GameSessionInfo(
    ushort RoomNumber,
    RoomStatus Status,
    byte Grade,
    string Name,
    GameType GameType,
    GameMode GameMode,
    ChanceMode ChanceMode,
    byte CurrentPlayers,
    byte MaxPlayers,
    string? Password,
    ushort MapId)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameSessionInfo>.RegistryId;

    /// <summary>Where the room stands. Never 0.</summary>
    public RoomStatus Status { get; } = Status != 0
        ? Status
        : throw new ArgumentOutOfRangeException(nameof(Status), "A room record with status 0 holds no room.");

    /// <summary>The room's name.</summary>
    public string Name { get; } = Name ?? throw new ArgumentNullException(nameof(Name));

    internal static GameSessionInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var roomNumber = reader.ReadUInt16();
        var status = (RoomStatus)reader.ReadByte();
        if (status == 0)
        {
            // The rest of the record is absent. Reading on finds nothing once refused.
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
        var password = reader.ReadBoolean() ? reader.ReadUtf16String() : null;
        var mapId = reader.ReadUInt16();
        return new GameSessionInfo(roomNumber, status, grade, name, gameType, gameMode, chanceMode, currentPlayers, maxPlayers, password, mapId);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(RoomNumber);
        writer.WriteByte((byte)Status);
        writer.WriteByte(Grade);
        writer.WriteUtf16String(Name);
        writer.WriteByte((byte)GameType);
        writer.WriteUInt16((ushort)GameMode);
        writer.WriteByte((byte)ChanceMode);
        writer.WriteByte(CurrentPlayers);
        writer.WriteByte(MaxPlayers);
        writer.WriteBoolean(Password is not null);
        if (Password is not null)
        {
            writer.WriteUtf16String(Password);
        }

        writer.WriteUInt16(MapId);
    }
}
