using System.Buffers;
using OpenTaan.Protocol.Data;

namespace OpenTaan.Protocol.Channels;

/// <summary>The answer to <see cref="PqGetChannelList"/>: the channels, as a data tree.</summary>
/// <remarks>
/// The tree travels in a <see cref="DataEnvelope"/> preceded by its 32-bit length. It is
/// written uncompressed, as the client has always been sent it. Its shape is the server's
/// to choose and the client's to understand: a root holding server nodes, each holding
/// its channels as attributes.
/// </remarks>
/// <param name="Channels">The root of the tree.</param>
[WireClass("PrGetChannelList")]
public sealed record PrGetChannelList(DataNode Channels) : IPacket
{
    // The most bytes the tree may unwrap to when read. The server never receives one;
    // tests and replays do.
    private const int MaximumTreeSize = 1 << 20;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrGetChannelList>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrGetChannelList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var channels = DataNode.ReadEnveloped(ref reader, MaximumTreeSize);
        return reader.Finish(new PrGetChannelList(channels));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        Channels.WriteEnveloped(writer);
    }
}
