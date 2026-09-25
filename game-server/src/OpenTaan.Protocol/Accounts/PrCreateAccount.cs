using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>The answer to <see cref="PqCreateAccount"/>.</summary>
/// <param name="Status">Whether the account was created.</param>
/// <param name="AccountSlot">
/// The slot the request was for. It must echo the request: the client marks that
/// slot's creation as done from this byte, and a wrong one leaves its login flow
/// waiting on a slot that already succeeded.
/// </param>
[WireClass("PrCreateAccount")]
public sealed record PrCreateAccount(CreateAccountStatus Status, byte AccountSlot) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrCreateAccount>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrCreateAccount> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var status = (CreateAccountStatus)reader.ReadByte();
        var accountSlot = reader.ReadByte();
        return reader.Finish(new PrCreateAccount(status, accountSlot));
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
