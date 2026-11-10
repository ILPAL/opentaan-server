using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The host has opened one of the room's slots, or closed it: sent to everyone in the room.</summary>
/// <remarks>
/// The client sets the slot's status by its index, without checking either: open makes it
/// empty and open, and closed makes it closed, whatever it held. So only an empty slot is
/// ever sent, and an index outside the room can be neither read nor built.
/// </remarks>
/// <param name="Open">True when the slot is now open, false when it is closed.</param>
/// <param name="SlotIndex">The slot, below <see cref="GameSlotInfo.SlotCount"/>.</param>
[WireClass("PcGameSession_OpenCloseSlot")]
public sealed record PcGameSessionOpenCloseSlot(bool Open, byte SlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionOpenCloseSlot>.RegistryId;

    /// <summary>The slot, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionOpenCloseSlot> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var open = reader.ReadBoolean();
        var slotIndex = reader.ReadByte();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGameSessionOpenCloseSlot(open, slotIndex));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteBoolean(Open);
        writer.WriteByte(SlotIndex);
    }
}
