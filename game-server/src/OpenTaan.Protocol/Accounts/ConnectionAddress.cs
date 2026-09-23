namespace OpenTaan.Protocol.Accounts;

/// <summary>A server address as the client carries it: an IPv4 address and a port.</summary>
/// <remarks>A plain structure inside a packet. It has no registry id of its own.</remarks>
/// <param name="Address">The IPv4 address, as the 32-bit value the client holds.</param>
/// <param name="Port">The TCP port.</param>
public sealed record ConnectionAddress(uint Address, ushort Port)
{
    /// <summary>Bytes it occupies.</summary>
    internal const int Size = sizeof(uint) + sizeof(ushort);

    internal static ConnectionAddress ReadFrom(ref PacketReader reader)
    {
        var address = reader.ReadUInt32();
        var port = reader.ReadUInt16();
        return new ConnectionAddress(address, port);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(Address);
        writer.WriteUInt16(Port);
    }
}
