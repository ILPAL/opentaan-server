using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A room was not created: the answer to <see cref="PqGameSessionCreate"/> when it is refused.</summary>
/// <param name="Status">Why. Only <see cref="CreateRefusal.Refused"/> can be written; see <see cref="CreateRefusal"/>.</param>
[WireClass("PrGameSession_CreateFail")]
public sealed record PrGameSessionCreateFail(CreateRefusal Status) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrGameSessionCreateFail>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrGameSessionCreateFail> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var status = (CreateRefusal)reader.ReadByte();
        return reader.Finish(new PrGameSessionCreateFail(status));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Status"/> is not one the enum names, such as 4, on which the client quits.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Status);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Status);
    }
}
