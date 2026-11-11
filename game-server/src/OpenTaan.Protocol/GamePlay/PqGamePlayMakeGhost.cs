using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>One of the client's own characters died in a shot, as the client played it back: make it a ghost.</summary>
/// <remarks>
/// Every client plays the death, but only the dying character's own client asks, and it
/// asks while it plays the shot, before it reports the shot finished. The server answers
/// everyone with <see cref="PcGamePlayMakeGhost"/>.
/// </remarks>
/// <param name="SlotIndex">The slot of the character that died, below <see cref="GameSlotInfo.SlotCount"/>.</param>
[WireClass("PqGamePlay_MakeGhost")]
public sealed record PqGamePlayMakeGhost(byte SlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGamePlayMakeGhost>.RegistryId;

    /// <summary>The slot of the character that died, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGamePlayMakeGhost> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PqGamePlayMakeGhost(slotIndex));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
    }
}
