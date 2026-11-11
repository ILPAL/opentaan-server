using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>The turn's character walks: where it has got to, sent while it moves and when it stops.</summary>
/// <remarks>
/// The server passes it to every other client in the match, which moves the character
/// there; the player's own client has moved it already. The client looks the character up
/// by slot without checking the index, so the slot is always one of a room's.
/// </remarks>
/// <param name="SlotIndex">The slot of the character walking, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the character has got to, across the map.</param>
/// <param name="Y">Where the character has got to, down the map.</param>
/// <param name="Direction">Which way the character faces.</param>
[WireClass("PcGamePlay_Move")]
public sealed record PcGamePlayMove(byte SlotIndex, int X, int Y, byte Direction) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayMove>.RegistryId;

    /// <summary>The slot of the character walking, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayMove> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        var direction = reader.ReadByte();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGamePlayMove(slotIndex, x, y, direction));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteByte(Direction);
    }
}
