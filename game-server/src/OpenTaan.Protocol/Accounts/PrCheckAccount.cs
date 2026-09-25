using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>The answer to <see cref="PqCheckAccount"/>.</summary>
/// <param name="Status">Whether the account exists.</param>
/// <param name="AccountSlot">
/// The slot the request was for. The client picks which step of its login flow to
/// complete from this byte, so it must echo the request's slot.
/// </param>
[WireClass("PrCheckAccount")]
public sealed record PrCheckAccount(CheckAccountStatus Status, byte AccountSlot) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrCheckAccount>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrCheckAccount> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var status = (CheckAccountStatus)reader.ReadByte();
        var accountSlot = reader.ReadByte();
        return reader.Finish(new PrCheckAccount(status, accountSlot));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Status);
        writer.WriteByte(AccountSlot);
    }
}
