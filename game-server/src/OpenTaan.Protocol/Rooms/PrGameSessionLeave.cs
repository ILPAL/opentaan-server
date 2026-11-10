using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The answer to <see cref="PqGameSessionLeave"/>.</summary>
/// <param name="Result">Whether the player left. Only the values the enum names can be written.</param>
[WireClass("PrGameSession_Leave")]
public sealed record PrGameSessionLeave(LeaveResult Result) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrGameSessionLeave>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrGameSessionLeave> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var result = (LeaveResult)reader.ReadByte();
        return reader.Finish(new PrGameSessionLeave(result));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Result"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Result);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Result);
    }
}
