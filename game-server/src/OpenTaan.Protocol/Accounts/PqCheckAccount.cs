using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>The client asks whether an account exists, before it logs in or creates one.</summary>
/// <param name="EmailAddress">The account's email address.</param>
/// <param name="AccountSlot">
/// Which of the client's two account slots this is for: 0 or 1 from a real client,
/// anything from a modified one. The reply must echo a valid slot.
/// </param>
[WireClass("PqCheckAccount")]
public sealed record PqCheckAccount(string EmailAddress, byte AccountSlot) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqCheckAccount>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqCheckAccount> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var emailAddress = reader.ReadUtf16String();
        var accountSlot = reader.ReadByte();
        return reader.Finish(new PqCheckAccount(emailAddress, accountSlot));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUtf16String(EmailAddress);
        writer.WriteByte(AccountSlot);
    }
}
