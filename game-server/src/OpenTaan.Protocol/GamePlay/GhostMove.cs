using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A dead character's ghost moving from one place to another, carried by the start of a turn.</summary>
/// <param name="SlotIndex">The ghost's character's slot, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="FromX">Where the ghost starts, across the map.</param>
/// <param name="FromY">Where the ghost starts, down the map.</param>
/// <param name="ToX">Where the ghost goes, across the map.</param>
/// <param name="ToY">Where the ghost goes, down the map.</param>
[WireClass("GhostMove")]
public sealed record GhostMove(byte SlotIndex, int FromX, int FromY, int ToX, int ToY)
{
    /// <summary>The bytes one occupies, its id included: every field has a fixed size.</summary>
    internal const int Size = sizeof(uint) + sizeof(byte) + (4 * sizeof(int));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GhostMove>.RegistryId;

    /// <summary>The ghost's character's slot, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    internal static GhostMove ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var fromX = reader.ReadInt32();
        var fromY = reader.ReadInt32();
        var toX = reader.ReadInt32();
        var toY = reader.ReadInt32();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return new GhostMove(slotIndex, fromX, fromY, toX, toY);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteInt32(FromX);
        writer.WriteInt32(FromY);
        writer.WriteInt32(ToX);
        writer.WriteInt32(ToY);
    }
}
