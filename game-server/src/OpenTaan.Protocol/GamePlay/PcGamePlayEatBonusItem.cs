using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A bonus item for the character whose turn ended, as its client drew it, in rooms whose chance mode gives one.</summary>
/// <remarks>
/// It has the shape of a box caught, with no box. The client does not give the item
/// itself: it sends this, and every client, the sender's among them, gives the character
/// the item when the server's copy comes back. So it goes to everyone in the match, the
/// sender included.
/// </remarks>
/// <param name="SlotIndex">The slot of the character that gets the item, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Catch">The item, as a catch of no box.</param>
/// <param name="TeamGauge">A change to a team's gauge that comes with it, or null for none.</param>
[WireClass("PcGamePlay_EatBonusItem")]
public sealed record PcGamePlayEatBonusItem(byte SlotIndex, GameScenarioCommandEatItemBox Catch, GameScenarioCommandTeamGauge? TeamGauge) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayEatBonusItem>.RegistryId;

    /// <summary>The slot of the character that gets the item, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = ItemCatch.Slot(SlotIndex);

    /// <summary>The item, as a catch of no box.</summary>
    public GameScenarioCommandEatItemBox Catch { get; } = Catch ?? throw new ArgumentNullException(nameof(Catch));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayEatBonusItem> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var (slotIndex, caught, teamGauge) = ItemCatch.Read(ref reader);
        return reader.Finish(new PcGamePlayEatBonusItem(slotIndex, caught, teamGauge));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        ItemCatch.Write(writer, SlotIndex, Catch, TeamGauge);
    }
}
