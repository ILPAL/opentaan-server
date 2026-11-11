using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A character fell off the map while walking, and died.</summary>
/// <remarks>
/// Only the dying character's own client sends it, having played the death already; the
/// server passes it to every other client in the match, then tells everyone the character
/// is a ghost. Like a shot it never goes back to its sender, whose client would send it
/// on again. The client looks the character up by slot without checking the index, so
/// the slot is always one of a room's. A death within a shot is the shot's own step, and
/// is reported apart; see <see cref="PqGamePlayMakeGhost"/>.
/// </remarks>
/// <param name="SlotIndex">The slot of the character that fell, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the character fell from, across the map.</param>
/// <param name="Y">Where the character fell from, down the map.</param>
[WireClass("PcGamePlay_DeathByDrop")]
public sealed record PcGamePlayDeathByDrop(byte SlotIndex, int X, int Y) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayDeathByDrop>.RegistryId;

    /// <summary>The slot of the character that fell, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayDeathByDrop> TryRead(ReadOnlySpan<byte> body)
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

        return reader.Finish(new PcGamePlayDeathByDrop(slotIndex, x, y));
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
