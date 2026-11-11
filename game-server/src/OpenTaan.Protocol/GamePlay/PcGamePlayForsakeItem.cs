using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A character throws away an item it holds.</summary>
/// <remarks>
/// Every client empties the character's item slot when the server's copy comes back, so
/// it goes to everyone in the match, the sender included. The client looks the character
/// up by slot without checking the index, so the slot is always one of a room's; an item
/// slot it cannot find is skipped.
/// </remarks>
/// <param name="SlotIndex">The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="ItemSlot">The character's item slot emptied.</param>
/// <param name="ItemId">The item thrown away.</param>
[WireClass("PcGamePlay_ForsakeItem")]
public sealed record PcGamePlayForsakeItem(byte SlotIndex, byte ItemSlot, ushort ItemId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayForsakeItem>.RegistryId;

    /// <summary>The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayForsakeItem> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var itemSlot = reader.ReadByte();
        var itemId = reader.ReadUInt16();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGamePlayForsakeItem(slotIndex, itemSlot, itemId));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(ItemSlot);
        writer.WriteUInt16(ItemId);
    }
}
