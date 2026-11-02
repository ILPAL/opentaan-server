using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// The account and login packets. Every value here is invented: none of it comes from
/// a recorded session.
/// </summary>
public sealed class AccountCodecTests
{
    [Fact]
    public void ACheckRequestCarriesTheEmailThenTheSlot()
    {
        var request = new PqCheckAccount("a@b", 1);

        byte[] expected = [.. Encoded.UInt32(PqCheckAccount.RegistryId), 0x03, 0x00, 0x00, 0x00, 0x61, 0x00, 0x40, 0x00, 0x62, 0x00, 0x01];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqCheckAccount.TryRead(expected).Packet);
    }

    [Fact]
    public void ACheckAnswerIsTheStatusThenTheSlot()
    {
        // Two different bytes, so that writing them the other way round is visible.
        var answer = new PrCheckAccount(CheckAccountStatus.NotFound, 0);

        byte[] expected = [.. Encoded.UInt32(PrCheckAccount.RegistryId), 0x01, 0x00];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrCheckAccount.TryRead(expected).Packet);
    }

    [Fact]
    public void ACreateRequestPutsTheGenderBeforeTheSlot()
    {
        // Both are single bytes, so swapping them would survive a round trip; only
        // the bytes show the order.
        var request = new PqCreateAccount("a", new Secret("h"), Gender.Female, 1);

        byte[] expected =
        [
            .. Encoded.UInt32(PqCreateAccount.RegistryId),
            0x01, 0x00, 0x00, 0x00, 0x61, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x68, 0x00,
            0x02, 0x01,
        ];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqCreateAccount.TryRead(expected).Packet);
    }

    [Fact]
    public void ACreateAnswerIsTheStatusThenTheSlot()
    {
        var answer = new PrCreateAccount(CreateAccountStatus.AlreadyExists, 0);

        byte[] expected = [.. Encoded.UInt32(PrCreateAccount.RegistryId), 0x01, 0x00];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrCreateAccount.TryRead(expected).Packet);
    }

    [Fact]
    public void ALoginRequestCarriesEachAccountThenTheAddressThenTheFlag()
    {
        var request = new PqUserLogin(
            [new LoginAccountInfo("a", new Secret("h"), 7, "x")],
            new ConnectionAddress(0x0A000001, 2180),
            1);

        byte[] expected =
        [
            .. Encoded.UInt32(PqUserLogin.RegistryId),
            0x01, 0x00, 0x00, 0x00,
            .. Encoded.UInt32(LoginAccountInfo.RegistryId),
            0x01, 0x00, 0x00, 0x00, 0x61,
            0x01, 0x00, 0x00, 0x00, 0x68,
            0x07, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x78, 0x00,
            0x01, 0x00, 0x00, 0x0A, 0x84, 0x08,
            0x01,
        ];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
    }

    [Fact]
    public void ALoginRequestWithTwoAccountsReadsBackAsItWasWritten()
    {
        var request = new PqUserLogin(
            [
                new LoginAccountInfo("first@example.test", new Secret("00112233445566778899aabbccddeeff"), 0, string.Empty),
                new LoginAccountInfo("second@example.test", new Secret("ffeeddccbbaa99887766554433221100"), 0, string.Empty),
            ],
            new ConnectionAddress(0, 0),
            0);
        var body = Encoded.Bytes(request.Write);

        var result = PqUserLogin.TryRead(body);

        Assert.True(result.Succeeded);
        Assert.Equal(request.Accounts, result.Packet.Accounts);
        Assert.Equal(body, Encoded.Bytes(result.Packet.Write));
    }

    [Fact]
    public void ALoginRequestKeepsItsPasswordsOutOfItsPrintout()
    {
        var request = new PqUserLogin(
            [new LoginAccountInfo("a@example.test", new Secret("00112233445566778899aabbccddeeff"), 0, string.Empty)],
            new ConnectionAddress(0, 0),
            0);

        Assert.DoesNotContain("00112233", request.Accounts[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAcceptedLoginRoundTrips()
    {
        var answer = new PrUserLoginSucceeded(
            LocalPlayerId: 42,
            Field24: 0,
            Accounts: [new AccountInfo(1001, AccountType.Normal, 0, 0, Gender.Male)],
            Catalogs: [CharacterCatalog.Empty],
            Field28: 0,
            Field2C: "Nam",
            Field30: 0);
        var body = Encoded.Bytes(answer.Write);

        var result = PrUserLogin.TryRead(body);

        var read = Assert.IsType<PrUserLoginSucceeded>(result.Packet);
        Assert.Equal(42u, read.LocalPlayerId);
        Assert.Equal(answer.Accounts, read.Accounts);
        Assert.Single(read.Catalogs);
        Assert.Equal("Nam", read.Field2C);
        Assert.Equal(body, Encoded.Bytes(read.Write));
    }

    [Fact]
    public void AnAccountDescriptionIsSixteenBytes()
    {
        var answer = new PrUserLoginSucceeded(0, 0, [new AccountInfo(0x01020304, AccountType.GameMaster, 0, 0, Gender.Female)], [], 0, string.Empty, 0);
        var body = Encoded.Bytes(answer.Write);

        // Id, flag, player id and Field24, then the account list's count.
        const int AccountStart = 4 + 1 + 4 + 4 + 4;
        byte[] expected = [.. Encoded.UInt32(AccountInfo.RegistryId), 0x04, 0x03, 0x02, 0x01, 0xFF, 0x00, 0, 0, 0, 0, 0, 0x02];

        Assert.Equal(expected, body.AsSpan(AccountStart, 16).ToArray());
    }

    [Fact]
    public void ARefusedLoginIsSeventeenBytesNotTheAcceptedLayout()
    {
        // The client reads four fields after a cleared flag. Anything longer leaves
        // bytes it never consumes, and it stops receiving without a word.
        var answer = new PrUserLoginFailed(LoginRefusalReason.Penalty, 0x00020001, 0x00040003);

        byte[] expected =
        [
            .. Encoded.UInt32(PrUserLogin.RegistryId),
            0x00,
            0x05, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x02, 0x00,
            0x03, 0x00, 0x04, 0x00,
        ];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrUserLogin.TryRead(expected).Packet);
    }

    [Fact]
    public void ALoginAnswerWhoseFlagIsNeitherZeroNorOneIsRefused()
    {
        var body = Encoded.Bytes(new PrUserLoginFailed(LoginRefusalReason.WrongPassword).Write);
        body[4] = 0x02;

        Assert.Equal(PacketError.UnexpectedValue, PrUserLogin.TryRead(body).Error);
    }

    [Fact]
    public void ALoginAnswerCarriesEachAccountsCharacters()
    {
        var catalog = new CharacterCatalog([CharacterSamples.Character], [CharacterSamples.Appearance]);
        var answer = new PrUserLoginSucceeded(0, 0, [], [catalog], 0, string.Empty, 0);

        byte[] expected =
        [
            .. Encoded.UInt32(PrUserLogin.RegistryId),
            0x01,
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(1), .. CharacterSamples.CatalogBytes,
            .. Encoded.UInt16(0),
            .. Encoded.Utf16(string.Empty),
            0x00,
        ];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));

        var read = Assert.IsType<PrUserLoginSucceeded>(PrUserLogin.TryRead(expected).Packet);
        Assert.Equal(CharacterSamples.Character, Assert.Single(Assert.Single(read.Catalogs).Characters));
    }
}
