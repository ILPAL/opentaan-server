using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player asks to leave its room.</summary>
/// <remarks>
/// The answer is <see cref="PrGameSessionLeave"/>. The client sends this only while it
/// shows a room, and asks for nothing else to do with rooms until it is answered, so
/// every request is answered. It also sends one on its way back to the lobby whenever
/// it still shows a room, and when the host has removed it from one.
/// </remarks>
/// <param name="RoomNumber">The room the client shows.</param>
/// <param name="Reason">Why the client leaves: 0 from the room screen, the lobby, or a removal by the host.</param>
[WireClass("PqGameSession_Leave")]
public sealed record PqGameSessionLeave(ushort RoomNumber, byte Reason) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionLeave>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionLeave> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var roomNumber = reader.ReadUInt16();
        var reason = reader.ReadByte();
        return reader.Finish(new PqGameSessionLeave(roomNumber, reason));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(RoomNumber);
        writer.WriteByte(Reason);
    }
}
