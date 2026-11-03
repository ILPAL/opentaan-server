using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>
/// An account's cash, the currency bought with real money. The client asks for it with
/// this packet, once per account slot, and the server answers with the same packet.
/// </summary>
/// <remarks>
/// The client stores the value it is sent at the slot the packet names, without checking
/// the slot, and the slot is a full 32-bit value here. The server must only ever send a
/// slot the connection holds.
/// </remarks>
/// <param name="Cash">The cash. Meaningless in a request.</param>
/// <param name="AccountSlot">Which of the client's accounts this is for.</param>
[WireClass("PcUpdateCash")]
public sealed record PcUpdateCash(uint Cash, uint AccountSlot) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcUpdateCash>.RegistryId;

    /// <summary>Reads a body: a request from the client, or an answer in tests and replays.</summary>
    public static PacketReadResult<PcUpdateCash> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var cash = reader.ReadUInt32();
        var accountSlot = reader.ReadUInt32();
        return reader.Finish(new PcUpdateCash(cash, accountSlot));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(Cash);
        writer.WriteUInt32(AccountSlot);
    }
}
