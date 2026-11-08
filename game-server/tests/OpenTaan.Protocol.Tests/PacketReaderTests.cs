using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// Hostile and malformed bodies. Everything the client sends is untrusted, so each
/// way a body can lie has to end in a refusal, not an exception or an allocation.
/// </summary>
public sealed class PacketReaderTests
{
    [Fact]
    public void ReadsLittleEndianValues()
    {
        var reader = new PacketReader([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F]);

        Assert.Equal(0x01, reader.ReadByte());
        Assert.Equal(0x0302, reader.ReadUInt16());
        Assert.Equal(0x07060504u, reader.ReadUInt32());
        Assert.Equal(0x0F0E0D0C0B0A0908ul, reader.ReadUInt64());
        Assert.Equal(PacketError.None, reader.Error);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadsA32BitFloat()
    {
        var reader = new PacketReader([0x00, 0x00, 0xC0, 0x3F]);

        Assert.Equal(1.5f, reader.ReadSingle());
        Assert.Equal(PacketError.None, reader.Error);
    }

    [Fact]
    public void ReadsASigned32BitValueInTwosComplement()
    {
        var reader = new PacketReader([0x18, 0xFC, 0xFF, 0xFF, 0x04, 0x03, 0x02, 0x01]);

        Assert.Equal(-1000, reader.ReadInt32());
        Assert.Equal(0x01020304, reader.ReadInt32());
        Assert.Equal(PacketError.None, reader.Error);
    }

    [Fact]
    public void RefusesABodyThatEndsBeforeItsLastField()
    {
        var body = Encoded.Bytes(new PrExistCharacter(true).Write);

        var result = PrExistCharacter.TryRead(body.AsSpan(0, body.Length - 1));

        Assert.False(result.Succeeded);
        Assert.Equal(PacketError.Truncated, result.Error);
    }

    [Fact]
    public void RefusesAnEmptyBody()
    {
        Assert.Equal(PacketError.Truncated, PqExistCharacter.TryRead([]).Error);
    }

    [Fact]
    public void RefusesABodyThatCarriesAnotherPacket()
    {
        var body = Encoded.Bytes(new PrExistCharacter(true).Write);

        Assert.Equal(PacketError.WrongRegistryId, PrCheckAccount.TryRead(body).Error);
    }

    [Fact]
    public void RefusesLeftoverBytes()
    {
        var body = Encoded.Bytes(new PrExistCharacter(true).Write).Append((byte)0).ToArray();

        Assert.Equal(PacketError.TrailingBytes, PrExistCharacter.TryRead(body).Error);
    }

    [Fact]
    public void RefusesAStringLongerThanTheRestOfTheBody()
    {
        // A four-billion-character name: refused on the count, before anything is
        // allocated for it.
        byte[] body = [.. Encoded.UInt32(PqExistCharacter.RegistryId), 0xFF, 0xFF, 0xFF, 0xFF, 0x41, 0x00];

        Assert.Equal(PacketError.CountOutOfRange, PqExistCharacter.TryRead(body).Error);
    }

    [Fact]
    public void RefusesAListLongerThanTheRestOfTheBody()
    {
        // Four billion accounts, and no room for even one.
        byte[] body = [.. Encoded.UInt32(PqUserLogin.RegistryId), 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0];

        Assert.Equal(PacketError.CountOutOfRange, PqUserLogin.TryRead(body).Error);
    }

    [Fact]
    public void RefusesAStringThatIsNotValidUtf16()
    {
        // One code unit: the first half of a surrogate pair with no second half.
        byte[] body = [.. Encoded.UInt32(PqExistCharacter.RegistryId), 0x01, 0x00, 0x00, 0x00, 0x00, 0xD8];

        Assert.Equal(PacketError.InvalidText, PqExistCharacter.TryRead(body).Error);
    }

    [Fact]
    public void RefusesAFlagThatIsNeitherZeroNorOne()
    {
        // Read as true, it would be written back as 1: a different body.
        byte[] body = [.. Encoded.UInt32(PrExistCharacter.RegistryId), 0x02];

        Assert.Equal(PacketError.UnexpectedValue, PrExistCharacter.TryRead(body).Error);
    }

    [Fact]
    public void KeepsTheFirstFailureAndReadsNothingAfterIt()
    {
        var reader = new PacketReader([0x01]);

        Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal(PacketError.Truncated, reader.Error);

        // One byte is still there, but a failed reader does not move.
        Assert.Equal(0, reader.ReadByte());
        reader.Fail(PacketError.InvalidText);
        Assert.Equal(PacketError.Truncated, reader.Error);
        Assert.Equal(1, reader.Remaining);
    }

    [Fact]
    public void RefusesANestedObjectOfTheWrongClass()
    {
        var login = new PqUserLogin(
            [new LoginAccountInfo("player@example.test", new Secret("0123456789abcdef"), 0, string.Empty)],
            new ConnectionAddress(0, 0),
            0);
        var body = Encoded.Bytes(login.Write);

        // Swap the account's class id, which follows the packet id and the count.
        Encoded.UInt32(AccountInfo.RegistryId).CopyTo(body, 8);

        Assert.Equal(PacketError.WrongRegistryId, PqUserLogin.TryRead(body).Error);
    }
}
