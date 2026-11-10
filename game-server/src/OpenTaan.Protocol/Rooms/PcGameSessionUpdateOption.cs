using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A room has a new host: sent to everyone in it when the host leaves.</summary>
/// <remarks>
/// Each client works out again whether it is the host, and if it is, shows the host's
/// buttons. A client that finds itself both the host and ready asks at once to be no
/// longer ready, which the server refuses from a host; so a new host's characters are
/// made not ready, and the room told so, before this is sent.
/// </remarks>
/// <param name="Option">Who hosts the room now.</param>
[WireClass("PcGameSession_UpdateOption")]
public sealed record PcGameSessionUpdateOption(GameSessionOption Option) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionUpdateOption>.RegistryId;

    /// <summary>Who hosts the room now.</summary>
    public GameSessionOption Option { get; } = Option ?? throw new ArgumentNullException(nameof(Option));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionUpdateOption> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var option = GameSessionOption.ReadFrom(ref reader);
        return reader.Finish(new PcGameSessionUpdateOption(option));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        Option.WriteTo(writer);
    }
}
