using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A ghost caught an item box: the box, and the item in it as the ghost's client drew it.</summary>
/// <remarks>
/// A box a missile catches is a step of the shot instead. A ghost's client does not give
/// the item itself: it sends this, and every client, the sender's among them, gives the
/// character the item when the server's copy comes back. So it goes to everyone in the
/// match, the sender included.
/// </remarks>
/// <param name="SlotIndex">The slot of the character whose ghost caught the box, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Catch">The catch: the box, by the id it appeared with, and the item.</param>
/// <param name="TeamGauge">A change to a team's gauge that comes with it, or null for none.</param>
[WireClass("PcGamePlay_EatItemBox")]
public sealed record PcGamePlayEatItemBox(byte SlotIndex, GameScenarioCommandEatItemBox Catch, GameScenarioCommandTeamGauge? TeamGauge) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayEatItemBox>.RegistryId;

    /// <summary>The slot of the character whose ghost caught the box, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = ItemCatch.Slot(SlotIndex);

    /// <summary>The catch: the box, by the id it appeared with, and the item.</summary>
    public GameScenarioCommandEatItemBox Catch { get; } = Catch ?? throw new ArgumentNullException(nameof(Catch));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayEatItemBox> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var (slotIndex, caught, teamGauge) = ItemCatch.Read(ref reader);
        return reader.Finish(new PcGamePlayEatItemBox(slotIndex, caught, teamGauge));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        ItemCatch.Write(writer, SlotIndex, Catch, TeamGauge);
    }
}
