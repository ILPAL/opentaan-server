using System.Buffers;

namespace OpenTaan.Protocol.PlayTime;

/// <summary>
/// Starts the client's play time over: every slot's played time goes back to 0, and a slot
/// whose time is counted is counted from now. It carries nothing but its id.
/// </summary>
[WireClass("PcResetPlayableTime")]
public sealed record PcResetPlayableTime : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcResetPlayableTime>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcResetPlayableTime> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PcResetPlayableTime());
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
