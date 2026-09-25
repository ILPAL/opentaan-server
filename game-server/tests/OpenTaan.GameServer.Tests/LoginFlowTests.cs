using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Connection;
using OpenTaan.Transport;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// The login stage end to end, through real sessions on loopback sockets. Every value
/// here is invented.
/// </summary>
public sealed class LoginFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly Secret RightPassword = new("digest-of-the-right-password");
    private static readonly Secret WrongPassword = new("digest-of-a-wrong-password");

    [Fact]
    public async Task TheServerSpeaksFirstWithAHandshakeThatNeverSwitchesEncryptionOff()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        var greeting = connection.Client.Greeting;
        var keys = new HandshakeKeys(greeting.Seed1, greeting.Seed2, greeting.Shift);

        Assert.NotEqual(OpenTaanCipher.PlaintextSeed, keys.BaseSeed);
    }

    [Fact]
    public async Task TheFirstPacketIsAnsweredWithoutBeingReadAndTheLoginFollows()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);

        // Its contents are not read, so any body with its id is answered.
        await connection.Client.SendBodyAsync((byte[])[.. Encoded.UInt32(PqFirstPacket.RegistryId), 0x01, 0x02, 0x03]);
        Assert.Equal(new PrFirstPacket(0, 1), await connection.Client.ReceiveAsync<PrFirstPacket>(PrFirstPacket.TryRead));

        await connection.Client.SendAsync(Login(("player@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    [Fact]
    public async Task AnAccountCreatedOnOneConnectionLogsInOnTheNext()
    {
        // The client checks and creates on one connection, closes it, and logs in on a
        // new one, so nothing about the account may live on the connection.
        var store = new InMemoryAccountStore();
        var services = SessionHarness.Services(requireRegistration: true, store: store);

        await using (var first = await SessionHarness.ConnectAsync(services, Cancel))
        {
            await first.Client.SendAsync(new PqCheckAccount("player@example.test", 1));
            Assert.Equal(
                new PrCheckAccount(CheckAccountStatus.NotFound, 1),
                await first.Client.ReceiveAsync<PrCheckAccount>(PrCheckAccount.TryRead));

            await first.Client.SendAsync(new PqCreateAccount("player@example.test", RightPassword, Gender.Female, 1));
            Assert.Equal(
                new PrCreateAccount(CreateAccountStatus.Created, 1),
                await first.Client.ReceiveAsync<PrCreateAccount>(PrCreateAccount.TryRead));

            await first.Client.DisposeAsync();
            Assert.Equal(CloseReason.PeerClosed, await first.ClosedReason());
        }

        await using var second = await SessionHarness.ConnectAsync(services, Cancel);

        await second.Client.SendAsync(new PqCheckAccount("player@example.test", 0));
        Assert.Equal(
            new PrCheckAccount(CheckAccountStatus.Exists, 0),
            await second.Client.ReceiveAsync<PrCheckAccount>(PrCheckAccount.TryRead));

        await second.Client.SendAsync(Login(("player@example.test", RightPassword)));
        var answer = Assert.IsType<PrUserLoginSucceeded>(await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        var created = (await store.FindAsync("player@example.test", Cancel))!;
        var account = Assert.Single(answer.Accounts);
        Assert.Equal(created.AccountId, account.AccountId);
        Assert.Equal(Gender.Female, account.Gender);
        Assert.Equal(AccountType.Normal, account.AccountType);
        Assert.Equal(created.LocalPlayerId, answer.LocalPlayerId);
        Assert.Single(answer.Catalogs);
        Assert.True(second.Session.IsLoggedIn);
    }

    [Fact]
    public async Task CreatingAnAccountThatExistsSaysSoAndChangesNothing()
    {
        var store = new InMemoryAccountStore();
        await store.CreateAsync("taken@example.test", RightPassword, Gender.Male, Cancel);
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(new PqCreateAccount("taken@example.test", WrongPassword, Gender.Female, 0));

        Assert.Equal(
            new PrCreateAccount(CreateAccountStatus.AlreadyExists, 0),
            await connection.Client.ReceiveAsync<PrCreateAccount>(PrCreateAccount.TryRead));
        Assert.True((await store.FindAsync("taken@example.test", Cancel))!.HasPassword(RightPassword));
    }

    [Fact]
    public async Task AWrongPasswordIsRefusedAsAWrongPasswordAndTheConnectionMayTryAgain()
    {
        var store = new InMemoryAccountStore();
        await store.CreateAsync("player@example.test", RightPassword, Gender.Male, Cancel);
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(Login(("player@example.test", WrongPassword)));
        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.WrongPassword),
            await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        await connection.Client.SendAsync(Login(("player@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    [Fact]
    public async Task AnUnknownAccountIsRefusedAsUnknownWhenRegistrationIsRequired()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: true), Cancel);

        await connection.Client.SendAsync(Login(("nobody@example.test", RightPassword)));

        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.UnknownAccount),
            await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.False(connection.Session.IsLoggedIn);
    }

    [Fact]
    public async Task OneWrongPasswordRefusesAWholeTwoAccountLogin()
    {
        var store = new InMemoryAccountStore();
        await store.CreateAsync("first@example.test", RightPassword, Gender.Male, Cancel);
        await store.CreateAsync("second@example.test", RightPassword, Gender.Female, Cancel);
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(Login(("first@example.test", RightPassword), ("second@example.test", WrongPassword)));

        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.WrongPassword),
            await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.False(connection.Session.IsLoggedIn);
    }

    [Fact]
    public async Task ATwoAccountLoginDescribesBothAccountsInSlotOrder()
    {
        var store = new InMemoryAccountStore();
        var first = await store.CreateAsync("first@example.test", RightPassword, Gender.Male, Cancel);
        var second = await store.CreateAsync("second@example.test", RightPassword, Gender.Female, Cancel);
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(Login(("second@example.test", RightPassword), ("first@example.test", RightPassword)));

        var answer = Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.Equal([second!.AccountId, first!.AccountId], answer.Accounts.Select(account => account.AccountId));
        Assert.Equal(second.LocalPlayerId, answer.LocalPlayerId);
        Assert.Equal(2, answer.Catalogs.Count);
    }

    [Fact]
    public async Task WithoutRegistrationAnyLoginCreatesItsAccountWithAnyPassword()
    {
        var store = new InMemoryAccountStore();
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false, store: store), Cancel);

        await connection.Client.SendAsync(Login(("new@example.test", WrongPassword)));

        var answer = Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        var account = Assert.Single(answer.Accounts);
        Assert.Equal(Gender.Male, account.Gender);
        Assert.NotNull(await store.FindAsync("new@example.test", Cancel));
    }

    [Fact]
    public async Task OnlyAnAccountTheConfigurationNamesLogsInAsAGameMaster()
    {
        var services = SessionHarness.Services(requireRegistration: false, gameMasters: ["master@example.test"]);
        await using var connection = await SessionHarness.ConnectAsync(services, Cancel);

        await connection.Client.SendAsync(Login(("player@example.test", RightPassword), ("master@example.test", RightPassword)));

        var answer = Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.Equal([AccountType.Normal, AccountType.GameMaster], answer.Accounts.Select(account => account.AccountType));
    }

    [Fact]
    public async Task AddressesAreComparedExactlyCaseIncluded()
    {
        var store = new InMemoryAccountStore();
        await store.CreateAsync("Player@example.test", RightPassword, Gender.Male, Cancel);
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(new PqCheckAccount("player@example.test", 0));

        Assert.Equal(CheckAccountStatus.NotFound, (await connection.Client.ReceiveAsync<PrCheckAccount>(PrCheckAccount.TryRead)).Status);
    }

    [Fact]
    public async Task ACheckForASlotNoRealClientHasIsAnsweredForSlotZero()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(), Cancel);

        await connection.Client.SendAsync(new PqCheckAccount("player@example.test", 2));

        Assert.Equal(
            new PrCheckAccount(CheckAccountStatus.NotFound, 0),
            await connection.Client.ReceiveAsync<PrCheckAccount>(PrCheckAccount.TryRead));
    }

    [Fact]
    public async Task ACreateForASlotNoRealClientHasFailsForSlotZeroAndCreatesNothing()
    {
        var store = new InMemoryAccountStore();
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(new PqCreateAccount("player@example.test", RightPassword, Gender.Male, 0xFF));

        Assert.Equal(
            new PrCreateAccount(CreateAccountStatus.Failed, 0),
            await connection.Client.ReceiveAsync<PrCreateAccount>(PrCreateAccount.TryRead));
        Assert.Null(await store.FindAsync("player@example.test", Cancel));
    }

    [Fact]
    public async Task AnAccountCreatedWithoutAGenderIsGivenOne()
    {
        var store = new InMemoryAccountStore();
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(store: store), Cancel);

        await connection.Client.SendAsync(new PqCreateAccount("player@example.test", RightPassword, Gender.Unspecified, 0));
        await connection.Client.ReceiveAsync<PrCreateAccount>(PrCreateAccount.TryRead);

        Assert.Equal(Gender.Male, (await store.FindAsync("player@example.test", Cancel))!.Gender);
    }

    [Fact]
    public async Task ALoginNamingNoAccountsIsRefusedAndNeverWithTheCodeThatQuitsTheGame()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);

        await connection.Client.SendAsync(Login());

        var answer = Assert.IsType<PrUserLoginFailed>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.Equal(LoginRefusalReason.UnknownAccount, answer.Reason);
    }

    [Fact]
    public async Task CheckNameIsAnsweredBeforeLoginAndAfter()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);

        await connection.Client.SendAsync(new PqExistCharacter("Somebody"));
        Assert.Equal(new PrExistCharacter(false), await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead));

        await connection.Client.SendAsync(Login(("player@example.test", RightPassword)));
        await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead);

        await connection.Client.SendAsync(new PqExistCharacter("Somebody"));
        Assert.Equal(new PrExistCharacter(false), await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead));
    }

    [Fact]
    public async Task ASecondLoginOnALoggedInConnectionClosesIt()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);

        await connection.Client.SendAsync(Login(("player@example.test", RightPassword)));
        await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead);
        await connection.Client.SendAsync(Login(("player@example.test", RightPassword)));

        Assert.Equal(CloseReason.SecondLogin, await connection.ClosedReason());
        Assert.True(await connection.Client.IsClosedByServerAsync());
    }

    [Fact]
    public async Task OneAccountInBothSlotsIsRefusedAsAlreadyInGame()
    {
        var store = new InMemoryAccountStore();
        await store.CreateAsync("player@example.test", RightPassword, Gender.Male, Cancel);
        var services = SessionHarness.Services(store: store);
        await using var connection = await SessionHarness.ConnectAsync(services, Cancel);

        await connection.Client.SendAsync(Login(("player@example.test", RightPassword), ("player@example.test", RightPassword)));

        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.AlreadyInGame),
            await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.False(connection.Session.IsLoggedIn);
        Assert.Null(services.LoggedIn.HolderOf((await store.FindAsync("player@example.test", Cancel))!.AccountId));
    }

    [Fact]
    public async Task AddressesDifferingOnlyInCaseAreTwoAccountsAndMayShareALogin()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);

        await connection.Client.SendAsync(Login(("Player@example.test", RightPassword), ("player@example.test", RightPassword)));

        var answer = Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.Equal(2, answer.Accounts.Count);
    }

    [Fact]
    public async Task ASecondLoginWhileTheFirstIsLiveIsRefusedAndTheFirstIsUndisturbed()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);

        await first.Client.SendAsync(Login(("player@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await first.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        await second.Client.SendAsync(Login(("player@example.test", RightPassword)));
        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.AlreadyInGame),
            await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        Assert.False(second.Session.IsLoggedIn);

        // The holder is still logged in and still answered.
        Assert.True(first.Session.IsLoggedIn);
        await first.Client.SendAsync(new PqExistCharacter("Somebody"));
        Assert.Equal(new PrExistCharacter(false), await first.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead));
    }

    [Fact]
    public async Task TheAccountInTheHoldersSecondSlotAlsoBlocks()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);

        await first.Client.SendAsync(Login(("first@example.test", RightPassword), ("second@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await first.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        await second.Client.SendAsync(Login(("other@example.test", RightPassword), ("second@example.test", RightPassword)));
        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.AlreadyInGame),
            await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    [Fact]
    public async Task ARefusedConnectionMayLogInWithAnotherAccount()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);

        await first.Client.SendAsync(Login(("player@example.test", RightPassword)));
        await first.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead);

        await second.Client.SendAsync(Login(("other@example.test", RightPassword), ("player@example.test", RightPassword)));
        Assert.IsType<PrUserLoginFailed>(await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        // The refused login took nothing, not even the account that was free.
        await second.Client.SendAsync(Login(("other@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    [Fact]
    public async Task AWrongPasswordAgainstALiveAccountSaysWrongPassword()
    {
        var store = new InMemoryAccountStore();
        await store.CreateAsync("player@example.test", RightPassword, Gender.Male, Cancel);
        var services = SessionHarness.Services(store: store);
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);

        await first.Client.SendAsync(Login(("player@example.test", RightPassword)));
        await first.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead);

        // Answering "already in game" here would tell anyone the account is online.
        await second.Client.SendAsync(Login(("player@example.test", WrongPassword)));
        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.WrongPassword),
            await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    [Fact]
    public async Task AClosedConnectionReleasesItsAccounts()
    {
        var services = SessionHarness.Services(requireRegistration: false);

        await using (var first = await SessionHarness.ConnectAsync(services, Cancel))
        {
            await first.Client.SendAsync(Login(("player@example.test", RightPassword)));
            await first.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead);

            await first.Client.DisposeAsync();
            Assert.Equal(CloseReason.PeerClosed, await first.ClosedReason());
        }

        await using var second = await SessionHarness.ConnectAsync(services, Cancel);
        await second.Client.SendAsync(Login(("player@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    [Fact]
    public async Task ASilentHolderIsClosedByTheIdleTimeoutAndReleasesItsAccounts()
    {
        // A holder that lost power sends nothing more, not even a close.
        var options = new GameServerOptions { IdleTimeout = TimeSpan.FromMilliseconds(300) };
        var services = SessionHarness.Services(requireRegistration: false, options: options);
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);

        await first.Client.SendAsync(Login(("player@example.test", RightPassword)));
        await first.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead);
        Assert.Equal(CloseReason.IdleTimeout, await first.ClosedReason());

        // The same accounts and store, but a timeout the newcomer cannot hit on a busy machine.
        await using var second = await SessionHarness.ConnectAsync(services with { Options = new GameServerOptions() }, Cancel);
        await second.Client.SendAsync(Login(("player@example.test", RightPassword)));
        Assert.IsType<PrUserLoginSucceeded>(await second.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    private static PqUserLogin Login(params (string EmailAddress, Secret PasswordHash)[] accounts) =>
        new(
            [.. accounts.Select(account => new LoginAccountInfo(account.EmailAddress, account.PasswordHash, 0, string.Empty))],
            new ConnectionAddress(0, 0),
            0);
}
