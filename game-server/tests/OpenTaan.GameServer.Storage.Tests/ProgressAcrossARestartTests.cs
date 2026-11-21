using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Sessions;
using OpenTaan.GameServer.Storage;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Tests.GameServer;

namespace OpenTaan.Tests.Storage;

/// <summary>
/// What a player made, through real connections to a server that keeps it in PostgreSQL,
/// still there once the server has started again: the account, its character and the
/// finished tutorial, so the player neither creates them again nor replays the tutorial.
/// Every value here is invented.
/// </summary>
public sealed class ProgressAcrossARestartTests(PostgresServer server)
{
    private static readonly Secret Password = new("5f4dcc3b5aa765d61d8327deb882cf99");
    private static readonly Secret WrongPassword = new("5f4dcc3b5aa765d61d8327deb882cf98");

    private static readonly AppearanceSlot[] EveryPart =
    [
        .. Enumerable.Range(0, AppearanceSlot.Count)
            .Select(part => new AppearanceSlot((uint)(0x10000 + part), (ushort)(0x2000 + part), (ushort)(0x3000 + part), (ushort)(0x4000 + part))),
    ];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAccountItsCharacterAndItsFinishedTutorialAreThereAfterARestart()
    {
        await using var database = await server.NewDatabaseAsync(Cancel);
        var before = Started(database);

        await using (var creating = await SessionHarness.ConnectAsync(before, Cancel))
        {
            await creating.Client.SendAsync(new PqCreateAccount("player@example.test", Password, Gender.Female, 0));
            Assert.Equal(
                new PrCreateAccount(CreateAccountStatus.Created, 0),
                await creating.Client.ReceiveAsync<PrCreateAccount>(PrCreateAccount.TryRead));
        }

        CharacterInfo character;
        CharacterAppear appearance;
        await using (var playing = await SessionHarness.ConnectAsync(before, Cancel))
        {
            Assert.Empty(Assert.Single((await LogInAsync(playing, Password)).Catalogs).Characters);

            await playing.Client.SendAsync(new PqNewCharacter(0, "Rồng", EveryPart));
            var created = await playing.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead);
            Assert.Equal(NewCharacterStatus.Created, created.Status);
            character = Assert.Single(created.Catalog!.Characters);
            appearance = Assert.Single(created.Catalog.Appearances);

            Assert.Equal([0], (await SelectAsync(playing, character.CharacterId)).TutorialFlags);
            await playing.Client.SendAsync(new PcTutorialFinished(character.CharacterId));
            Assert.Equal([1], (await SelectAsync(playing, character.CharacterId)).TutorialFlags);

            await playing.Client.DisposeAsync();
            Assert.Equal(CloseReason.PeerClosed, await playing.ClosedReason());
        }

        // Started again: new connections to the database, and nothing else carried over.
        var after = Started(database);
        await using var later = await SessionHarness.ConnectAsync(after, Cancel);

        await later.Client.SendAsync(Login(WrongPassword));
        Assert.Equal(
            new PrUserLoginFailed(LoginRefusalReason.WrongPassword),
            await later.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        var catalog = Assert.Single((await LogInAsync(later, Password)).Catalogs);
        Assert.Equal(character, Assert.Single(catalog.Characters));
        Assert.Equal(appearance, Assert.Single(catalog.Appearances));
        Assert.Equal([1], (await SelectAsync(later, character.CharacterId)).TutorialFlags);

        await later.Client.SendAsync(new PqConfirmName(0, "RỒNG"));
        Assert.Equal(new PrConfirmName(ConfirmNameStatus.Taken), await later.Client.ReceiveAsync<PrConfirmName>(PrConfirmName.TryRead));
    }

    // A server as it starts: every store over new connections to the database.
    private static SessionServices Started(TestDatabase database) => SessionHarness.Services(
        requireRegistration: true,
        store: new PostgresAccountStore(database.Open(), Options.Create(new AccountOptions())),
        characterStore: new PostgresCharacterStore(database.Open(), Options.Create(SessionHarness.ShippedCharacterOptions())));

    private static PqUserLogin Login(Secret password) => new(
        [new LoginAccountInfo("player@example.test", password, 0, string.Empty)],
        new ConnectionAddress(0, 0),
        0);

    private static async Task<PrUserLoginSucceeded> LogInAsync(Connection connection, Secret password)
    {
        await connection.Client.SendAsync(Login(password));
        return Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    private static async Task<PrSelectCharacter> SelectAsync(Connection connection, uint characterId)
    {
        await connection.Client.SendAsync(new PqSelectCharacter([characterId]));
        return await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);
    }
}
