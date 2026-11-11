using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A change to one character's health, carried by the start of a turn.</summary>
/// <remarks>
/// The client adds the change to the character's health, and for a loss shows the damage
/// and plays the character's hurt animation. It does not make a character whose health the
/// change takes to nothing die.
/// </remarks>
/// <param name="SlotIndex">The character's slot, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Field09">A value the client does not use for the change; 0 here.</param>
/// <param name="HpDelta">How much health the character gains, or loses when negative.</param>
[WireClass("HpUpdate")]
public sealed record HpUpdate(byte SlotIndex, byte Field09, int HpDelta)
{
    /// <summary>The bytes one occupies, its id included: every field has a fixed size.</summary>
    internal const int Size = sizeof(uint) + (2 * sizeof(byte)) + sizeof(int);

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<HpUpdate>.RegistryId;

    /// <summary>The character's slot, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    internal static HpUpdate ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var field09 = reader.ReadByte();
        var hpDelta = reader.ReadInt32();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return new HpUpdate(slotIndex, field09, hpDelta);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(Field09);
        writer.WriteInt32(HpDelta);
    }
}
