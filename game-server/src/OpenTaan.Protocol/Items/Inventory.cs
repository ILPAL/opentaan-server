namespace OpenTaan.Protocol.Items;

/// <summary>One of a character's inventories, as character selection carries them.</summary>
/// <remarks>
/// Only the empty inventory is supported so far, which is what a new character has.
/// The items arrive with the inventory packets. Until then an inventory that holds
/// anything is refused as <see cref="PacketError.Unsupported"/> rather than skipped,
/// because an item's size cannot be known without reading it.
/// </remarks>
/// <param name="InventoryTypeId">Which inventory this is, and whose.</param>
/// <param name="Category">Meaning not established yet.</param>
/// <param name="SlotType">Meaning not established yet.</param>
[WireClass("Inventory")]
public sealed record Inventory(uint InventoryTypeId, byte Category, byte SlotType)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<Inventory>.RegistryId;

    /// <summary>The fewest bytes it can occupy: its id, its fields and an empty item list.</summary>
    internal const int MinimumSize = sizeof(uint) + sizeof(uint) + sizeof(byte) + sizeof(byte) + sizeof(uint);

    internal static Inventory ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var inventoryTypeId = reader.ReadUInt32();
        var category = reader.ReadByte();
        var slotType = reader.ReadByte();
        if (reader.ReadUInt32() != 0)
        {
            reader.Fail(PacketError.Unsupported);
        }

        return new Inventory(inventoryTypeId, category, slotType);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(InventoryTypeId);
        writer.WriteByte(Category);
        writer.WriteByte(SlotType);
        writer.WriteUInt32(0);
    }
}
