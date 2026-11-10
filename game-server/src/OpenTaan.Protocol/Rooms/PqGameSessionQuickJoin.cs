using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player asks to be put in any room that will have it.</summary>
/// <remarks>
/// The answer is the room it was put in, <see cref="PcGameSessionInit"/>, or a refusal,
/// <see cref="PrGameSessionQuickJoinFail"/>. Until one of them arrives the client asks for
/// nothing else to do with rooms, so every request is answered. Which room is the
/// server's choice: the client names none.
/// </remarks>
/// <param name="RoomNumber">Always <see cref="AnyRoom"/> from the client.</param>
/// <param name="Integrity">The client's report on its own code.</param>
[WireClass("PqGameSession_QuickJoin")]
public sealed record PqGameSessionQuickJoin(ushort RoomNumber, IntegrityReport Integrity) : IPacket
{
    /// <summary>The room number the client sends: the only value it sends, naming no room.</summary>
    public const ushort AnyRoom = 0xFFFF;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionQuickJoin>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionQuickJoin> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var roomNumber = reader.ReadUInt16();
        var integrity = IntegrityReport.ReadFrom(ref reader);
        return reader.Finish(new PqGameSessionQuickJoin(roomNumber, integrity));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(RoomNumber);
        Integrity.WriteTo(writer);
    }
}
