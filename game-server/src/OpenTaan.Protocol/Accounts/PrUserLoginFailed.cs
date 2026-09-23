namespace OpenTaan.Protocol.Accounts;

/// <summary>The login was refused. The client shows the reason and disconnects.</summary>
/// <param name="Reason">Why, which selects the message the player sees.</param>
/// <param name="Detail1">Used only by <see cref="LoginRefusalReason.Penalty"/>, whose message it helps format.</param>
/// <param name="Detail2">Used only by <see cref="LoginRefusalReason.Penalty"/>, whose message it helps format.</param>
public sealed record PrUserLoginFailed(LoginRefusalReason Reason, uint Detail1 = 0, uint Detail2 = 0) : PrUserLogin
{
    internal static PrUserLoginFailed ReadFields(ref PacketReader reader)
    {
        var reason = (LoginRefusalReason)reader.ReadUInt32();
        var detail1 = reader.ReadUInt32();
        var detail2 = reader.ReadUInt32();
        return new PrUserLoginFailed(reason, detail1, detail2);
    }

    private protected override void WriteFields(PacketWriter writer)
    {
        writer.WriteBoolean(false);
        writer.WriteUInt32((uint)Reason);
        writer.WriteUInt32(Detail1);
        writer.WriteUInt32(Detail2);
    }
}
