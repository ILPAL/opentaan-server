using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Tests.Protocol;

public sealed class SecretTests
{
    [Fact]
    public void PrintsAsStars()
    {
        Assert.Equal("***", new Secret("0123456789abcdef").ToString());
    }

    [Fact]
    public void KeepsItsValueOutOfThePacketThatCarriesIt()
    {
        // A record prints every member, which is how credentials end up in logs.
        var request = new PqCreateAccount("player@example.test", new Secret("0123456789abcdef"), Gender.Female, 1);

        var printed = request.ToString();

        Assert.Contains("PasswordHash = ***", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparesByValue()
    {
        Assert.Equal(new Secret("same"), new Secret("same"));
        Assert.NotEqual(new Secret("same"), new Secret("SAME"));
        Assert.Equal("same", new Secret("same").Reveal());
    }
}
