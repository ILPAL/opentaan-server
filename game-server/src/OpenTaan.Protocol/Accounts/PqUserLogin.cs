using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>The client logs in: one set of credentials per account slot in use.</summary>
/// <param name="Accounts">The credentials, one entry per account the player is logging in with.</param>
/// <param name="ServerAddress">A server address the client includes. Zero from a fresh login.</param>
/// <param name="GameGuardFlag">A flag from the client's anti-cheat layer.</param>
[WireClass("PqUserLogin")]
public sealed record PqUserLogin(IReadOnlyList<LoginAccountInfo> Accounts, ConnectionAddress ServerAddress, byte GameGuardFlag)
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqUserLogin>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqUserLogin> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var accounts = reader.ReadList(LoginAccountInfo.MinimumSize, LoginAccountInfo.ReadFrom);
        var serverAddress = ConnectionAddress.ReadFrom(ref reader);
        var gameGuardFlag = reader.ReadByte();
        return reader.Finish(new PqUserLogin(accounts, serverAddress, gameGuardFlag));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(ServerAddress);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Accounts, static (list, account) => account.WriteTo(list));
        ServerAddress.WriteTo(writer);
        writer.WriteByte(GameGuardFlag);
    }
}
