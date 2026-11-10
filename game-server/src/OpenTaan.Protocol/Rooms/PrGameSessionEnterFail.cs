using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player was not let into a room: the answer to <see cref="PqGameSessionEnter"/> when it is refused.</summary>
/// <param name="Reason">Why. Only the values the enum names can be written.</param>
[WireClass("PrGameSession_EnterFail")]
public sealed record PrGameSessionEnterFail(EnterRefusal Reason) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrGameSessionEnterFail>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrGameSessionEnterFail> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var reason = (EnterRefusal)reader.ReadByte();
        return reader.Finish(new PrGameSessionEnterFail(reason));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Reason"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Reason);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Reason);
    }
}
