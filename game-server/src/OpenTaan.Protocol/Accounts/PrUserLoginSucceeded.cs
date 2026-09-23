using OpenTaan.Protocol.Characters;

namespace OpenTaan.Protocol.Accounts;

/// <summary>The login was accepted.</summary>
/// <remarks>
/// Fields whose meaning is not established yet are named after where the client
/// keeps them, such as <see cref="Field24"/>.
/// </remarks>
/// <param name="LocalPlayerId">The id the client uses for this player in rooms and games.</param>
/// <param name="Field24">Not used by the client as far as is known.</param>
/// <param name="Accounts">The accounts that logged in, one per slot.</param>
/// <param name="Catalogs">Each account's characters.</param>
/// <param name="Field28">Meaning not established yet; possibly status flags.</param>
/// <param name="Field2C">Meaning not established yet; possibly a user name.</param>
/// <param name="Field30">Meaning not established yet; possibly a permission level.</param>
public sealed record PrUserLoginSucceeded(
    uint LocalPlayerId,
    uint Field24,
    IReadOnlyList<AccountInfo> Accounts,
    IReadOnlyList<CharacterCatalog> Catalogs,
    ushort Field28,
    string Field2C,
    byte Field30) : PrUserLogin
{
    internal static PrUserLoginSucceeded ReadFields(ref PacketReader reader)
    {
        var localPlayerId = reader.ReadUInt32();
        var field24 = reader.ReadUInt32();
        var accounts = reader.ReadList(AccountInfo.Size, AccountInfo.ReadFrom);
        var catalogs = reader.ReadList(CharacterCatalog.MinimumSize, CharacterCatalog.ReadFrom);
        var field28 = reader.ReadUInt16();
        var field2C = reader.ReadUtf16String();
        var field30 = reader.ReadByte();
        return new PrUserLoginSucceeded(localPlayerId, field24, accounts, catalogs, field28, field2C, field30);
    }

    private protected override void WriteFields(PacketWriter writer)
    {
        writer.WriteBoolean(true);
        writer.WriteUInt32(LocalPlayerId);
        writer.WriteUInt32(Field24);
        writer.WriteList(Accounts, static (list, account) => account.WriteTo(list));
        writer.WriteList(Catalogs, static (list, catalog) => catalog.WriteTo(list));
        writer.WriteUInt16(Field28);
        writer.WriteUtf16String(Field2C);
        writer.WriteByte(Field30);
    }
}
