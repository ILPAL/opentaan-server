namespace OpenTaan.Protocol.Characters;

/// <summary>
/// The layout the client shares between its answers to character creation and
/// deletion: a status, the account slot, and the account's characters if they changed.
/// </summary>
/// <remarks>
/// The client stores the catalog at the slot this reply names without checking the
/// slot, so the slot must be one the connection holds. The presence flag is always
/// written, even with no catalog after it.
/// </remarks>
internal static class CharacterChangeReply
{
    public static (byte Status, byte AccountSlot, CharacterCatalog? Catalog) ReadFields(ref PacketReader reader)
    {
        var status = reader.ReadByte();
        var accountSlot = reader.ReadByte();
        var catalog = reader.ReadBoolean() ? CharacterCatalog.ReadFrom(ref reader) : null;
        return (status, accountSlot, catalog);
    }

    public static void WriteFields(PacketWriter writer, byte status, byte accountSlot, CharacterCatalog? catalog)
    {
        writer.WriteByte(status);
        writer.WriteByte(accountSlot);
        writer.WriteBoolean(catalog is not null);
        catalog?.WriteTo(writer);
    }
}
