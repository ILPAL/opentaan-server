using System.Buffers;

namespace OpenTaan.Protocol.Channels;

/// <summary>The client asks to enter a channel.</summary>
/// <remarks>
/// After a move answered <see cref="ChannelMoveResult.Entered"/>, a request for the
/// channel the client is already in is not sent at all: the client goes straight to the
/// lobby.
/// </remarks>
/// <param name="Forced">Set whenever <paramref name="Context"/> is not zero.</param>
/// <param name="ChannelId">The channel to enter.</param>
/// <param name="Context">A value the client passes with some moves. Meaning not established yet; 0 from the channel list.</param>
/// <param name="ClientVersionType">Which build of the client is asking.</param>
[WireClass("PqChannel_MoveTo")]
public sealed record PqChannelMoveTo(bool Forced, ushort ChannelId, uint Context, uint ClientVersionType) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqChannelMoveTo>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqChannelMoveTo> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var forced = reader.ReadBoolean();
        var channelId = reader.ReadUInt16();
        var context = reader.ReadUInt32();
        var clientVersionType = reader.ReadUInt32();
        return reader.Finish(new PqChannelMoveTo(forced, channelId, context, clientVersionType));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteBoolean(Forced);
        writer.WriteUInt16(ChannelId);
        writer.WriteUInt32(Context);
        writer.WriteUInt32(ClientVersionType);
    }
}
