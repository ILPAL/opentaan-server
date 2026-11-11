using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A character has left a match that goes on without it: sent to everyone still in the match.</summary>
/// <remarks>
/// The client takes the character off the field, which would otherwise stand there, idle,
/// for the rest of the match. It ignores a slot outside a room's, so only a room's slots
/// are sent.
/// </remarks>
/// <param name="SlotIndex">The slot of the character that left, below <see cref="GameSlotInfo.SlotCount"/>.</param>
[WireClass("PcGamePlay_LeaveUser")]
public sealed record PcGamePlayLeaveUser(byte SlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayLeaveUser>.RegistryId;

    /// <summary>The slot of the character that left, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGamePlayLeaveUser> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGamePlayLeaveUser(slotIndex));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
    }
}
