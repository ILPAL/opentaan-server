using System.Buffers;

namespace OpenTaan.Protocol.Connection;

/// <summary>The answer to <see cref="PqFirstPacket"/>.</summary>
/// <remarks>
/// No recorded session carries one, so the layout comes from the client's own reader.
/// A real client answered with status 0 and value 1 goes on to log in; what it does
/// with other values is not established yet.
/// </remarks>
/// <param name="Status">0 lets the client go on to log in.</param>
/// <param name="ServerValue">Meaning not established yet; servers have sent 1.</param>
/// <param name="TrailingBool">Read by the client and discarded. Send 0.</param>
[WireClass("PrFirstPacket")]
public sealed record PrFirstPacket(byte Status, uint ServerValue, byte TrailingBool = 0) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrFirstPacket>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests do.</summary>
    public static PacketReadResult<PrFirstPacket> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var status = reader.ReadByte();
        var serverValue = reader.ReadUInt32();
        var trailingBool = reader.ReadByte();
        return reader.Finish(new PrFirstPacket(status, serverValue, trailingBool));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(Status);
        writer.WriteUInt32(ServerValue);
        writer.WriteByte(TrailingBool);
    }
}
