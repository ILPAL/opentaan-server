using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The host of a room asks to change its map, its rules or its chances, or a mission's difficulty.</summary>
/// <remarks>
/// <para>
/// The client's options dialog sends this when the map, the rules or the chances change,
/// and stays locked until the room's options come back as
/// <see cref="PcGameSessionChangeGameOption"/>, so every request from the host is
/// answered. The host's difficulty buttons send it too, with the room's map, rules and
/// chances, and wait for nothing.
/// </para>
/// <para>
/// The chances are not the host's own choice: the dialog asks for bonus chances with items
/// and basic ones without. Outside a mission room the difficulty means nothing: the dialog
/// sends 0xFF there, and the difficulty buttons one less.
/// </para>
/// <para>
/// Every value is read as it is, even one the client never sends, so that a request the
/// server refuses is still answered rather than closing the connection.
/// </para>
/// </remarks>
/// <param name="MapId">The map picked from the client's list, or a mission's own number in a mission room.</param>
/// <param name="GameMode">The rules asked for.</param>
/// <param name="ChanceMode">The chances asked for.</param>
/// <param name="Difficulty">A mission's difficulty, 0 to 2. Meaningless outside a mission room.</param>
[WireClass("PqGameSession_ChangeGameOption")]
public sealed record PqGameSessionChangeGameOption(ushort MapId, GameMode GameMode, ChanceMode ChanceMode, byte Difficulty) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionChangeGameOption>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionChangeGameOption> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var mapId = reader.ReadUInt16();
        var gameMode = (GameMode)reader.ReadUInt16();
        var chanceMode = (ChanceMode)reader.ReadByte();
        var difficulty = reader.ReadByte();
        return reader.Finish(new PqGameSessionChangeGameOption(mapId, gameMode, chanceMode, difficulty));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(MapId);
        writer.WriteUInt16((ushort)GameMode);
        writer.WriteByte((byte)ChanceMode);
        writer.WriteByte(Difficulty);
    }
}
