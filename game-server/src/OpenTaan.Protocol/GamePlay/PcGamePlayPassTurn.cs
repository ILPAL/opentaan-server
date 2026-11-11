using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>The turn's character passes: its player asked to, or its client's turn timer ran out.</summary>
/// <remarks>
/// The client plays a pass on its own screen at once and sends it; the server passes it to
/// every other client in the match, then ends the turn. Like a shot it never goes back to
/// its sender, whose client would send it on again. The client looks the character up by
/// slot without checking the index, so the slot is always one of a room's.
/// </remarks>
/// <param name="SlotIndex">The slot of the character passing, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the character stands, across the map.</param>
/// <param name="Y">Where the character stands, down the map.</param>
/// <param name="Direction">Which way the character faces.</param>
/// <param name="TotalStats">The sum of the character's stats, as its client reckons them.</param>
[WireClass("PcGamePlay_PassTurn")]
public sealed record PcGamePlayPassTurn(byte SlotIndex, int X, int Y, byte Direction, uint TotalStats) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayPassTurn>.RegistryId;

    /// <summary>The slot of the character passing, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayPassTurn> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        var direction = reader.ReadByte();
        var totalStats = reader.ReadUInt32();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGamePlayPassTurn(slotIndex, x, y, direction, totalStats));
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
        writer.WriteUInt32(TotalStats);
    }
}
