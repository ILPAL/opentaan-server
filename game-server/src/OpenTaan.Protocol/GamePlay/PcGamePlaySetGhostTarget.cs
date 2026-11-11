using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A dead character's player picks where its ghost goes; the ghost moves there with the start of the next turn.</summary>
/// <remarks>
/// It goes to everyone in the match, the sender included. Every start of a turn after it
/// carries the ghost's move, from where its client last reported it to this point, until
/// its client reports it has arrived; see <see cref="PcGamePlaySetGhostPos"/>. The client
/// looks the character up by slot without checking the index, so the slot is always one
/// of a room's.
/// </remarks>
/// <param name="SlotIndex">The slot of the ghost's character, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the ghost goes, across the map.</param>
/// <param name="Y">Where the ghost goes, down the map.</param>
[WireClass("PcGamePlay_SetGhostTarget")]
public sealed record PcGamePlaySetGhostTarget(byte SlotIndex, int X, int Y) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlaySetGhostTarget>.RegistryId;

    /// <summary>The slot of the ghost's character, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlaySetGhostTarget> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGamePlaySetGhostTarget(slotIndex, x, y));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
    }
}
