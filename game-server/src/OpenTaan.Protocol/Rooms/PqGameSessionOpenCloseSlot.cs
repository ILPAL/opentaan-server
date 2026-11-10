using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The host of a room asks to open one of its slots to players entering, or to close it.</summary>
/// <remarks>
/// The client lets the host click any slot, and waits for no answer: the room is told the
/// change with <see cref="PcGameSessionOpenCloseSlot"/>.
/// </remarks>
/// <param name="Open">True to open the slot, false to close it.</param>
/// <param name="SlotIndex">The slot, as the client sends it: any byte is read.</param>
[WireClass("PqGameSession_OpenCloseSlot")]
public sealed record PqGameSessionOpenCloseSlot(bool Open, byte SlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionOpenCloseSlot>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionOpenCloseSlot> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var open = reader.ReadBoolean();
        var slotIndex = reader.ReadByte();
        return reader.Finish(new PqGameSessionOpenCloseSlot(open, slotIndex));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteBoolean(Open);
        writer.WriteByte(SlotIndex);
    }
}
