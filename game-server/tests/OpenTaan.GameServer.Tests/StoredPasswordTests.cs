using System.Text;
using OpenTaan.GameServer.Accounts;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Tests.GameServer;

/// <summary>A password as the server keeps it: the hash, and the text it is kept as.</summary>
public sealed class StoredPasswordTests
{
    // What the client sends for a password: 32 lower-case hex digits of an MD5 of it.
    private static readonly Secret Digest = new("5f4dcc3b5aa765d61d8327deb882cf99");
    private static readonly byte[] Salt = [.. Enumerable.Range(1, StoredPassword.SaltSize).Select(value => (byte)value)];

    // Sixteen and thirty-two zero bytes, as base64.
    private const string ZeroSalt = "AAAAAAAAAAAAAAAAAAAAAA==";
    private const string ZeroHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Theory]
    [InlineData("passwd", "salt", 1, "55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc")]
    [InlineData("Password", "NaCl", 80_000, "4ddcd8f60b98be21830cee5ef22701f9641a4418d04c0414aeff08876b34ab56")]
    public void TheHashIsPbkdf2WithSha256AsRfc7914PublishesIt(string password, string salt, int iterations, string expected)
    {
        // RFC 7914 section 11 lists 64 bytes of output; a hash here is the first 32.
        var hash = StoredPassword.HashOf(new Secret(password), Encoding.ASCII.GetBytes(salt), iterations);

        Assert.Equal(Convert.FromHexString(expected), hash);
    }

    [Fact]
    public void TheTextItIsKeptAsNeverHoldsTheDigest()
    {
        var stored = StoredPassword.Derive(Digest, Salt, 1_000);

        Assert.DoesNotContain(Digest.Reveal(), stored.Format(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Digest.Reveal())), stored.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheDigestItWasMadeFromMatches()
    {
        var stored = StoredPassword.Derive(Digest, Salt, 1_000);

        Assert.True(stored.Matches(Digest));
        Assert.False(stored.Matches(new Secret("5f4dcc3b5aa765d61d8327deb882cf98")));
        Assert.False(stored.Matches(new Secret(Digest.Reveal() + "0")));
        Assert.False(stored.Matches(new Secret(Digest.Reveal()[..^1])));
        Assert.False(stored.Matches(new Secret(string.Empty)));
    }

    [Fact]
    public void TheDigestInCapitalsDoesNotMatch()
    {
        // The client always writes its digits in lower case, so the text is compared as sent.
        var stored = StoredPassword.Derive(Digest, Salt, 1_000);

        Assert.False(stored.Matches(new Secret(Digest.Reveal().ToUpperInvariant())));
    }

    [Fact]
    public void ItsTextReadsBackAsThePasswordItWas()
    {
        var stored = StoredPassword.Derive(Digest, Salt, 1_000);
        var text = stored.Format();

        Assert.True(StoredPassword.TryParse(text, out var read));
        Assert.Equal(text, read.Format());
        Assert.Equal(1_000, read.Iterations);
        Assert.True(read.Matches(Digest));
        Assert.False(read.Matches(new Secret("5f4dcc3b5aa765d61d8327deb882cf98")));
    }

    [Fact]
    public void ItsTextNamesTheSchemeTheRoundsTheSaltAndTheHashInThatOrder()
    {
        const string Text = "pbkdf2-sha256$1000$" + ZeroSalt + "$" + ZeroHash;

        Assert.True(StoredPassword.TryParse(Text, out var read));
        Assert.Equal(1_000, read.Iterations);
        Assert.Equal(Text, read.Format());
    }

    [Theory]
    [InlineData("")]
    [InlineData("5f4dcc3b5aa765d61d8327deb882cf99")]
    [InlineData("pbkdf2-sha1$1000$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("PBKDF2-SHA256$1000$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1000$" + ZeroSalt)]
    [InlineData("pbkdf2-sha256$1000$" + ZeroSalt + "$" + ZeroHash + "$")]
    [InlineData("pbkdf2-sha256$0$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$-1000$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$+1000$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$01000$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$ 1000$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1e3$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$2147483648$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$$" + ZeroSalt + "$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAA$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAAA=$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1000$" + ZeroSalt + "$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2-sha256$1000$" + ZeroSalt + "$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("pbkdf2-sha256$1000$!!!!AAAAAAAAAAAAAAAAAA==$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA$" + ZeroHash)]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAA AAAAAAAAAAA==$" + ZeroHash)]
    public void AnyOtherTextIsRefused(string text)
    {
        Assert.False(StoredPassword.TryParse(text, out var read));
        Assert.Null(read);
    }

    [Fact]
    public void NoTextIsRefused()
    {
        Assert.False(StoredPassword.TryParse(null, out _));
    }

    [Fact]
    public void ItPrintsAsStarsAndSoDoesTheAccountThatHoldsIt()
    {
        var stored = StoredPassword.Derive(Digest, Salt, 1_000);
        var account = new Account(1001, 1, "player@example.test", stored, Gender.Male, 0);

        Assert.Equal("***", stored.ToString());
        Assert.Contains("Password = ***", account.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(stored.Format(), account.ToString(), StringComparison.Ordinal);
    }
}
