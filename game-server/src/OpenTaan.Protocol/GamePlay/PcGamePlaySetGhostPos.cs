using System.Buffers;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>The client reports where its ghost stopped, once the ghost has finished a move.</summary>
/// <remarks>
/// The client sends it for its own ghost only, and expects no answer. The tutorial
/// sends it too, when its ghost lesson moves the player's ghost, although that game is
/// played on the client alone.
/// </remarks>
/// <param name="SlotIndex">The game slot of the ghost's character.</param>
/// <param name="X">Where the ghost stopped, across the map.</param>
/// <param name="Y">Where the ghost stopped, down the map.</param>
/// <param name="Direction">Which way the ghost faces.</param>
[WireClass("PcGamePlay_SetGhostPos")]
public sealed record PcGamePlaySetGhostPos(byte SlotIndex, int X, int Y, byte Direction) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlaySetGhostPos>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlaySetGhostPos> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        var direction = reader.ReadByte();
        return reader.Finish(new PcGamePlaySetGhostPos(slotIndex, x, y, direction));
    }

    /// <summary>Writes the body, as the client would.</summary>
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
