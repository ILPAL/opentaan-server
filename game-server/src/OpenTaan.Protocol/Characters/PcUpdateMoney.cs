using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>Which of a character's currencies a <see cref="PcUpdateMoney"/> carries.</summary>
public enum MoneyKind : byte
{
    /// <summary>Gold.</summary>
    Gold = 0,

    /// <summary>Nothing is kept: the client only redraws the account's cash.</summary>
    CashShown = 1,

    /// <summary>League money.</summary>
    LeagueMoney = 2,
}

/// <summary>A character's gold or league money, as it is now: sent to its own connection after it changes.</summary>
/// <remarks>
/// <para>
/// The value is the whole balance, which the client keeps in place of its own, never an
/// amount to add. This is the only packet that changes a character's gold or league money
/// once it has logged in, and the client redraws them at once.
/// </para>
/// <para>
/// The slot is the account's place in the connection's login, 0 or, for a second account,
/// 1, never a room's slot. The client ignores a packet for a slot it has no character in.
/// Only for gold does it read the last two values: when told to, it says in the chat that
/// the character has reached a limit, naming the number given.
/// </para>
/// </remarks>
/// <param name="CharacterSlot">The account's place in the login, whose character this is.</param>
/// <param name="Kind">Which currency the value is.</param>
/// <param name="Value">The whole balance.</param>
/// <param name="LimitShown">The number the chat names when <paramref name="ShowsLimit"/> is set.</param>
/// <param name="ShowsLimit">Whether the chat says the character's gold has reached a limit.</param>
[WireClass("PcUpdateMoney")]
public sealed record PcUpdateMoney(byte CharacterSlot, MoneyKind Kind, uint Value, uint LimitShown, bool ShowsLimit) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcUpdateMoney>.RegistryId;

    /// <summary>A character's gold, with nothing said in the chat.</summary>
    public static PcUpdateMoney GoldOf(byte characterSlot, uint gold) => new(characterSlot, MoneyKind.Gold, gold, LimitShown: 0, ShowsLimit: false);

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcUpdateMoney> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterSlot = reader.ReadByte();
        var kind = (MoneyKind)reader.ReadByte();
        var value = reader.ReadUInt32();
        var limitShown = reader.ReadUInt32();
        var showsLimit = reader.ReadBoolean();
        if (!Enum.IsDefined(kind))
        {
            reader.Fail(PacketError.UnexpectedValue);
            return new PacketReadResult<PcUpdateMoney>(null, reader.Error);
        }

        return reader.Finish(new PcUpdateMoney(characterSlot, kind, value, limitShown, showsLimit));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(CharacterSlot);
        writer.WriteByte((byte)Kind);
        writer.WriteUInt32(Value);
        writer.WriteUInt32(LimitShown);
        writer.WriteBoolean(ShowsLimit);
    }
}
