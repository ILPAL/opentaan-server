namespace OpenTaan.Protocol.Accounts;

/// <summary>One account's credentials, as <see cref="PqUserLogin"/> carries them.</summary>
/// <param name="EmailAddress">The account's email address, one byte per character.</param>
/// <param name="PasswordHash">The password as the client hashed it, one byte per character. Prints as <c>***</c>.</param>
/// <param name="UnknownNumber">Meaning not established yet.</param>
/// <param name="UnknownText">Meaning not established yet.</param>
[WireClass("LoginAccountInfo")]
public sealed record LoginAccountInfo(string EmailAddress, Secret PasswordHash, uint UnknownNumber, string UnknownText)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<LoginAccountInfo>.RegistryId;

    /// <summary>The fewest bytes it can occupy: its id, three empty strings and a number.</summary>
    internal const int MinimumSize = 5 * sizeof(uint);

    internal static LoginAccountInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var emailAddress = reader.ReadByteString();
        var passwordHash = new Secret(reader.ReadByteString());
        var unknownNumber = reader.ReadUInt32();
        var unknownText = reader.ReadUtf16String();
        return new LoginAccountInfo(emailAddress, passwordHash, unknownNumber, unknownText);
    }

    internal void WriteTo(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(PasswordHash);

        writer.WriteUInt32(RegistryId);
        writer.WriteByteString(EmailAddress);
        writer.WriteByteString(PasswordHash.Reveal());
        writer.WriteUInt32(UnknownNumber);
        writer.WriteUtf16String(UnknownText);
    }
}
