using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>
/// A room's map, rules and chances, and a mission's difficulty: sent to everyone in the room
/// when the host changes them, and to the host alone when the change is refused.
/// </summary>
/// <remarks>
/// The host's options dialog stays locked from its request until this arrives, so a
/// refusal is answered too, with the room as it is. Every client takes the map, the rules
/// and the chances, and the difficulty only in a mission room.
/// </remarks>
/// <param name="MapId">The map its match is played on: a mission's own number in a mission room.</param>
/// <param name="GameMode">The rules its match is played by.</param>
/// <param name="ChanceMode">The chances its match gives.</param>
/// <param name="Difficulty">The mission's difficulty. The client reads it only in a mission room.</param>
[WireClass("PcGameSession_ChangeGameOption")]
public sealed record PcGameSessionChangeGameOption(ushort MapId, GameMode GameMode, ChanceMode ChanceMode, byte Difficulty) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionChangeGameOption>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionChangeGameOption> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var mapId = reader.ReadUInt16();
        var gameMode = (GameMode)reader.ReadUInt16();
        var chanceMode = (ChanceMode)reader.ReadByte();
        var difficulty = reader.ReadByte();
        return reader.Finish(new PcGameSessionChangeGameOption(mapId, gameMode, chanceMode, difficulty));
    }

    /// <summary>Writes the body.</summary>
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
