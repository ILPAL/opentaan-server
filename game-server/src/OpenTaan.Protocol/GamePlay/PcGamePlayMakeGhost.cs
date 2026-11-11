using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A character is a ghost from now on: sent to everyone in the match, where the character last stood.</summary>
/// <remarks>
/// The client looks the character up by slot without checking the index, so the slot is
/// always one of a room's.
/// </remarks>
/// <param name="SlotIndex">The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the ghost appears, across the map.</param>
/// <param name="Y">Where the ghost appears, down the map.</param>
/// <param name="Team">The character's team.</param>
[WireClass("PcGamePlay_MakeGhost")]
public sealed record PcGamePlayMakeGhost(byte SlotIndex, int X, int Y, byte Team) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayMakeGhost>.RegistryId;

    /// <summary>The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGamePlayMakeGhost> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        var team = reader.ReadByte();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return reader.Finish(new PcGamePlayMakeGhost(slotIndex, x, y, team));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteByte(Team);
    }
}
