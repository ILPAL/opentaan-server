namespace OpenTaan.Protocol.Characters;

/// <summary>
/// One of the sixteen parts a character's appearance is made of: the item worn there
/// and three variants of it, all looked up in the client's game data.
/// </summary>
/// <remarks>
/// An appearance always has exactly <see cref="Count"/> of these, in a fixed order the
/// client gives meaning to: the character creator fills the parts it offers and leaves
/// the rest zero, and the last part is the character's partner.
/// </remarks>
/// <param name="ItemId">The item worn in this part, or 0 for none.</param>
/// <param name="Variant1">The first variant, such as a colour.</param>
/// <param name="Variant2">The second variant.</param>
/// <param name="Variant3">The third variant.</param>
public readonly record struct AppearanceSlot(uint ItemId, ushort Variant1, ushort Variant2, ushort Variant3)
{
    /// <summary>How many parts every appearance has.</summary>
    public const int Count = 16;

    /// <summary>Bytes one part occupies.</summary>
    internal const int Size = sizeof(uint) + (3 * sizeof(ushort));

    /// <summary>Bytes all the parts of one appearance occupy.</summary>
    internal const int TotalSize = Count * Size;

    /// <summary>
    /// <paramref name="slots"/>, if it holds exactly <see cref="Count"/> parts. The client
    /// reads that many whatever the server meant, so any other number cannot be sent.
    /// </summary>
    /// <exception cref="ArgumentException">There are not exactly <see cref="Count"/> parts.</exception>
    internal static IReadOnlyList<AppearanceSlot> Complete(IReadOnlyList<AppearanceSlot> slots, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(slots, parameterName);

        return slots.Count == Count
            ? slots
            : throw new ArgumentException($"An appearance has exactly {Count} parts, not {slots.Count}.", parameterName);
    }

    internal static AppearanceSlot[] ReadAll(ref PacketReader reader)
    {
        var slots = new AppearanceSlot[Count];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = new AppearanceSlot(reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
        }

        return slots;
    }

    internal static void WriteAll(PacketWriter writer, IReadOnlyList<AppearanceSlot> slots)
    {
        foreach (var slot in slots)
        {
            writer.WriteUInt32(slot.ItemId);
            writer.WriteUInt16(slot.Variant1);
            writer.WriteUInt16(slot.Variant2);
            writer.WriteUInt16(slot.Variant3);
        }
    }
}
