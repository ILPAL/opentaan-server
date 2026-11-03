using System.Buffers;

namespace OpenTaan.Protocol.Channels;

/// <summary>
/// The client asks for the channel list. Sent once, when its channel-selection screen
/// opens after character selection. It carries nothing but its id.
/// </summary>
[WireClass("PqGetChannelList")]
public sealed record PqGetChannelList : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGetChannelList>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGetChannelList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PqGetChannelList());
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
