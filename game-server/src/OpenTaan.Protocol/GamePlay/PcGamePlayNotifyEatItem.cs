using System.Buffers;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>
/// The client reports where an item it picked up went: which of the character's item
/// slots received it.
/// </summary>
/// <remarks>
/// Every client in a game places a picked-up item itself, in the first empty item slot
/// of the character that picked it up, but only the player's own client sends this.
/// It expects no answer. The tutorial sends it too: the tutorial plays a game on the
/// client alone, and still reports its pickups.
/// </remarks>
/// <param name="SlotIndex">The game slot of the character that picked the item up.</param>
/// <param name="ItemSlot">The character's item slot that received it.</param>
/// <param name="ItemId">The item.</param>
[WireClass("PcGamePlay_NotifyEatItem")]
public sealed record PcGamePlayNotifyEatItem(byte SlotIndex, byte ItemSlot, ushort ItemId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayNotifyEatItem>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayNotifyEatItem> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var itemSlot = reader.ReadByte();
        var itemId = reader.ReadUInt16();
        return reader.Finish(new PcGamePlayNotifyEatItem(slotIndex, itemSlot, itemId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(ItemSlot);
        writer.WriteUInt16(ItemId);
    }
}
