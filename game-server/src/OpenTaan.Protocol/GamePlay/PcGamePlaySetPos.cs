using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>The turn's character is placed: where it stands, sent by its client during the turn.</summary>
/// <remarks>
/// The server passes it to every other client in the match, which puts the character
/// there. The client looks the character up by slot without checking the index, so the
/// slot is always one of a room's.
/// </remarks>
/// <param name="SlotIndex">The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the character stands, across the map.</param>
/// <param name="Y">Where the character stands, down the map.</param>
/// <param name="Direction">Which way the character faces.</param>
[WireClass("PcGamePlay_SetPos")]
public sealed record PcGamePlaySetPos(byte SlotIndex, int X, int Y, byte Direction) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlaySetPos>.RegistryId;

    /// <summary>The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlaySetPos> TryRead(ReadOnlySpan<byte> body)
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

        return reader.Finish(new PcGamePlaySetPos(slotIndex, x, y, direction));
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
