using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player in a room asks to move its characters to the other team.</summary>
/// <remarks>
/// The client asks only in a room of teams, only while it is not ready, and only for the
/// team its characters are not on. It waits for no answer: the room is told the move with
/// <see cref="PcGameSessionChangeTeam"/>.
/// </remarks>
/// <param name="Team">The team asked for: 1 or 2 from the client, though any byte is read.</param>
[WireClass("PqGameSession_ChangeTeam")]
public sealed record PqGameSessionChangeTeam(byte Team) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionChangeTeam>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionChangeTeam> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var team = reader.ReadByte();
        return reader.Finish(new PqGameSessionChangeTeam(team));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(Team);
    }
}
