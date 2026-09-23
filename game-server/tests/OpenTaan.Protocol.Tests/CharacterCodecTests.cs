using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.Protocol;

public sealed class CharacterCodecTests
{
    [Fact]
    public void AnExistenceRequestCarriesTheNameAsUtf16()
    {
        var request = new PqExistCharacter("Nam");

        byte[] expected = [.. Encoded.UInt32(PqExistCharacter.RegistryId), 0x03, 0x00, 0x00, 0x00, 0x4E, 0x00, 0x61, 0x00, 0x6D, 0x00];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqExistCharacter.TryRead(expected).Packet);
    }

    [Fact]
    public void AnExistenceRequestMayNameNothing()
    {
        var result = PqExistCharacter.TryRead(Encoded.Bytes(new PqExistCharacter(string.Empty).Write));

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Packet.CharacterName);
    }

    [Theory]
    [InlineData(true, 0x01)]
    [InlineData(false, 0x00)]
    public void AnExistenceAnswerIsOneByteThatIsZeroOrOne(bool exists, byte expected)
    {
        var body = Encoded.Bytes(new PrExistCharacter(exists).Write);

        Assert.Equal([.. Encoded.UInt32(PrExistCharacter.RegistryId), expected], body);
        Assert.Equal(exists, PrExistCharacter.TryRead(body).Packet?.Exists);
    }
}
