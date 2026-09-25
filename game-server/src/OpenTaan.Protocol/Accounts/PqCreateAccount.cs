using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>The client asks to create an account.</summary>
/// <param name="EmailAddress">The new account's email address.</param>
/// <param name="PasswordHash">The password as the client hashed it. Prints as <c>***</c>.</param>
/// <param name="Gender">The gender chosen for the account. A real client never sends <see cref="Gender.Unspecified"/>.</param>
/// <param name="AccountSlot">Which of the client's two account slots this is for. The reply must echo it.</param>
[WireClass("PqCreateAccount")]
public sealed record PqCreateAccount(string EmailAddress, Secret PasswordHash, Gender Gender, byte AccountSlot) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqCreateAccount>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqCreateAccount> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var emailAddress = reader.ReadUtf16String();
        var passwordHash = new Secret(reader.ReadUtf16String());

        // Gender first, then the slot: the client writes them in that order.
        var gender = (Gender)reader.ReadByte();
        var accountSlot = reader.ReadByte();
        return reader.Finish(new PqCreateAccount(emailAddress, passwordHash, gender, accountSlot));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(PasswordHash);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUtf16String(EmailAddress);
        writer.WriteUtf16String(PasswordHash.Reveal());
        writer.WriteByte((byte)Gender);
        writer.WriteByte(AccountSlot);
    }
}
