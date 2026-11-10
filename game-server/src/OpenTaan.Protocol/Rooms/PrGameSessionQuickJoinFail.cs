using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>No room would take the player: the answer to <see cref="PqGameSessionQuickJoin"/> when it is refused. It carries nothing but its id.</summary>
[WireClass("PrGameSession_QuickJoinFail")]
public sealed record PrGameSessionQuickJoinFail : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrGameSessionQuickJoinFail>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrGameSessionQuickJoinFail> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PrGameSessionQuickJoinFail());
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
