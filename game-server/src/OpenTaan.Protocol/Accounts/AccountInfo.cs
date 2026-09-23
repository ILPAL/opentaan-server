namespace OpenTaan.Protocol.Accounts;

/// <summary>One account, as <see cref="PrUserLoginSucceeded"/> describes it to the client.</summary>
/// <remarks>
/// Sent only at login, so the client holds these values for the whole session.
/// </remarks>
/// <param name="AccountId">The account's id.</param>
/// <param name="AccountType">Whether the account is a player's or a game master's.</param>
/// <param name="Reserved1">Not read by the client. Send 0.</param>
/// <param name="Reserved2">Not read by the client. Send 0.</param>
/// <param name="Gender">
/// The account's gender. Never send <see cref="Gender.Unspecified"/>: it leaves the
/// character creator with no appearance options.
/// </param>
[WireClass("AccountInfo")]
public sealed record AccountInfo(uint AccountId, AccountType AccountType, uint Reserved1, byte Reserved2, Gender Gender)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<AccountInfo>.RegistryId;

    /// <summary>Bytes it occupies.</summary>
    internal const int Size = 16;

    internal static AccountInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var accountId = reader.ReadUInt32();
        var accountType = (AccountType)reader.ReadUInt16();
        var reserved1 = reader.ReadUInt32();
        var reserved2 = reader.ReadByte();
        var gender = (Gender)reader.ReadByte();
        return new AccountInfo(accountId, accountType, reserved1, reserved2, gender);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(AccountId);
        writer.WriteUInt16((ushort)AccountType);
        writer.WriteUInt32(Reserved1);
        writer.WriteByte(Reserved2);
        writer.WriteByte((byte)Gender);
    }
}
