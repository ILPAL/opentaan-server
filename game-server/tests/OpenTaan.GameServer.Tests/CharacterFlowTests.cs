using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Items;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// The character stage end to end, through real sessions on loopback sockets: naming,
/// creating, selecting and deleting characters. Every value here is invented.
/// </summary>
public sealed class CharacterFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Every part chosen, each different, so it shows which parts a new character keeps.
    private static readonly AppearanceSlot[] EveryPart =
    [
        .. Enumerable.Range(0, AppearanceSlot.Count)
            .Select(part => new AppearanceSlot((uint)(0x10000 + part), (ushort)(0x2000 + part), (ushort)(0x3000 + part), (ushort)(0x4000 + part))),
    ];

    [Fact]
    public async Task ACharacterIsNamedCreatedSelectedAndListedAtTheNextLogin()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        CharacterInfo created;

        await using (var connection = await SessionHarness.ConnectAsync(services, Cancel))
        {
            await LogInAsync(connection, "player@example.test");

            await connection.Client.SendAsync(new PqConfirmName(0, "Rồng"));
            Assert.Equal(ConfirmNameStatus.Available, (await connection.Client.ReceiveAsync<PrConfirmName>(PrConfirmName.TryRead)).Status);

            await connection.Client.SendAsync(new PqNewCharacter(0, "Rồng", EveryPart));
            var answer = await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead);
            Assert.Equal((NewCharacterStatus.Created, (byte)0), (answer.Status, answer.AccountSlot));

            created = Assert.Single(answer.Catalog!.Characters);
            Assert.Equal(
                new CharacterInfo(created.CharacterId, "Rồng", 1, 0, 0, 300, 0, Gender.Male, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty),
                created);

            var appearance = Assert.Single(answer.Catalog.Appearances);
            Assert.Equal((Gender.Male, created.CharacterId, (ushort)1), (appearance.Gender, appearance.Field08, appearance.Field0C));

            await connection.Client.SendAsync(new PqExistCharacter("rỒNG"));
            Assert.True((await connection.Client.ReceiveAsync<PrExistCharacter>(PrExistCharacter.TryRead)).Exists);

            await connection.Client.SendAsync(new PqSelectCharacter([created.CharacterId]));
            var selection = await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);
            Assert.Equal(created, Assert.Single(selection.Characters));
            Assert.Equal([created.CharacterId], connection.Session.SelectedCharacterIds);

            await connection.Client.DisposeAsync();
            Assert.Equal(CloseReason.PeerClosed, await connection.ClosedReason());
        }

        await using var next = await SessionHarness.ConnectAsync(services, Cancel);
        var login = await LogInAsync(next, "player@example.test");
        Assert.Equal(created, Assert.Single(Assert.Single(login.Catalogs).Characters));
    }

    [Fact]
    public async Task ANewCharacterKeepsOnlyThePartsTheCreatorOffers()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);
        await LogInAsync(connection, "player@example.test");

        await connection.Client.SendAsync(new PqNewCharacter(0, "Kimi", EveryPart));
        var appearance = Assert.Single((await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead)).Catalog!.Appearances);

        var offered = SessionHarness.ShippedCharacterOptions().CreatorParts;
        Assert.Equal(
            EveryPart.Select((part, position) => offered.Contains(position) ? part : default),
            appearance.Slots);
    }

    [Theory]
    [InlineData("Kim")]
    [InlineData("Kim Anh")]
    [InlineData("Kim0123456789ABCD")]
    public async Task ANameTheClientRefusesIsAnsweredTakenAndCannotBeCreated(string name)
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);
        await LogInAsync(connection, "player@example.test");

        await connection.Client.SendAsync(new PqConfirmName(0, name));
        Assert.Equal(ConfirmNameStatus.Taken, (await connection.Client.ReceiveAsync<PrConfirmName>(PrConfirmName.TryRead)).Status);

        await connection.Client.SendAsync(new PqNewCharacter(0, name, EveryPart));
        Assert.Equal(
            new PrNewCharacter(NewCharacterStatus.Failed, 0, Catalog: null),
            await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead));
    }

    [Fact]
    public async Task ANameInUseIsAnsweredTakenAndItsCreationSaysSo()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);
        await LogInAsync(first, "first@example.test");
        await LogInAsync(second, "second@example.test");
        await CreateAsync(first, 0, "Kimi");

        await second.Client.SendAsync(new PqConfirmName(0, "KIMI"));
        Assert.Equal(ConfirmNameStatus.Taken, (await second.Client.ReceiveAsync<PrConfirmName>(PrConfirmName.TryRead)).Status);

        await second.Client.SendAsync(new PqNewCharacter(0, "KIMI", EveryPart));
        Assert.Equal(
            new PrNewCharacter(NewCharacterStatus.NameTaken, 0, Catalog: null),
            await second.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead));
    }

    [Fact]
    public async Task BeforeLoginANameIsTakenAndNothingIsCreated()
    {
        var store = new InMemoryCharacterStore();
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(characterStore: store), Cancel);

        await connection.Client.SendAsync(new PqConfirmName(0, "Kimi"));
        Assert.Equal(ConfirmNameStatus.Taken, (await connection.Client.ReceiveAsync<PrConfirmName>(PrConfirmName.TryRead)).Status);

        await connection.Client.SendAsync(new PqNewCharacter(0, "Kimi", EveryPart));
        Assert.Equal(
            new PrNewCharacter(NewCharacterStatus.Failed, 0, Catalog: null),
            await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead));
        Assert.False(await store.IsNameTakenAsync("Kimi", Cancel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(255)]
    public async Task ACreationForASlotTheConnectionDoesNotHoldFailsForSlotZeroAndCreatesNothing(byte slot)
    {
        // The client stores the answer's characters at the slot it names, unchecked.
        var store = new InMemoryCharacterStore();
        await using var connection = await SessionHarness.ConnectAsync(
            SessionHarness.Services(requireRegistration: false, characterStore: store), Cancel);
        await LogInAsync(connection, "player@example.test");

        await connection.Client.SendAsync(new PqNewCharacter(slot, "Kimi", EveryPart));

        Assert.Equal(
            new PrNewCharacter(NewCharacterStatus.Failed, 0, Catalog: null),
            await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead));
        Assert.False(await store.IsNameTakenAsync("Kimi", Cancel));
    }

    [Fact]
    public async Task ASecondCharacterForAnAccountIsRefused()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);
        await LogInAsync(connection, "player@example.test");
        await CreateAsync(connection, 0, "Kimi");

        await connection.Client.SendAsync(new PqNewCharacter(0, "Rồng", EveryPart));

        Assert.Equal(
            new PrNewCharacter(NewCharacterStatus.Failed, 0, Catalog: null),
            await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead));
    }

    [Fact]
    public async Task ATwoAccountConnectionCreatesAndSelectsForEachSlotInOrder()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);
        await LogInAsync(connection, "first@example.test", "second@example.test");
        var first = await CreateAsync(connection, 0, "Kimi");
        var second = await CreateAsync(connection, 1, "Rồng");

        await connection.Client.SendAsync(new PqSelectCharacter([first.CharacterId, second.CharacterId]));
        var selection = await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);

        Assert.Equal([first, second], selection.Characters);
        Assert.Equal([new Inventory(2, 0, 0), new Inventory(5, 0, 0)], selection.ItemInventories);
        Assert.Equal([new Inventory(1, 0, 0), new Inventory(4, 0, 0)], selection.EquipmentInventories);
        Assert.Equal([new Inventory(3, 0, 0), new Inventory(6, 0, 0)], selection.BingoInventories);
        Assert.Equal([0, 0], selection.TutorialFlags);
        Assert.Equal([1, 1], selection.SlotUnlockFlags);
    }

    [Fact]
    public async Task SelectingAnotherAccountsCharacterClosesTheConnection()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        await using var owner = await SessionHarness.ConnectAsync(services, Cancel);
        await using var other = await SessionHarness.ConnectAsync(services, Cancel);
        await LogInAsync(owner, "owner@example.test");
        await LogInAsync(other, "other@example.test");
        var character = await CreateAsync(owner, 0, "Kimi");

        await other.Client.SendAsync(new PqSelectCharacter([character.CharacterId]));

        Assert.Equal(CloseReason.InvalidSelection, await other.ClosedReason());
        Assert.Empty(other.Session.SelectedCharacterIds);
    }

    [Fact]
    public async Task SelectingOneCharacterTooManyOrBeforeLoginClosesTheConnection()
    {
        var services = SessionHarness.Services(requireRegistration: false);
        await using var player = await SessionHarness.ConnectAsync(services, Cancel);
        await LogInAsync(player, "player@example.test");
        var character = await CreateAsync(player, 0, "Kimi");

        await player.Client.SendAsync(new PqSelectCharacter([character.CharacterId, character.CharacterId]));
        Assert.Equal(CloseReason.InvalidSelection, await player.ClosedReason());

        await using var stranger = await SessionHarness.ConnectAsync(services, Cancel);
        await stranger.Client.SendAsync(new PqSelectCharacter([character.CharacterId]));
        Assert.Equal(CloseReason.InvalidSelection, await stranger.ClosedReason());
    }

    [Fact]
    public async Task DeletingACharacterAnswersWithWhatIsLeftAndFreesItsName()
    {
        await using var connection = await SessionHarness.ConnectAsync(SessionHarness.Services(requireRegistration: false), Cancel);
        await LogInAsync(connection, "player@example.test");
        var character = await CreateAsync(connection, 0, "Kimi");
        await connection.Client.SendAsync(new PqSelectCharacter([character.CharacterId]));
        await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);

        await connection.Client.SendAsync(new PqDelCharacter(0, character.CharacterId));
        var answer = await connection.Client.ReceiveAsync<PrDelCharacter>(PrDelCharacter.TryRead);

        Assert.Equal((DeleteCharacterStatus.Deleted, (byte)0), (answer.Status, answer.AccountSlot));
        Assert.Empty(answer.Catalog!.Characters);
        Assert.Empty(connection.Session.SelectedCharacterIds);

        await connection.Client.SendAsync(new PqConfirmName(0, "Kimi"));
        Assert.Equal(ConfirmNameStatus.Available, (await connection.Client.ReceiveAsync<PrConfirmName>(PrConfirmName.TryRead)).Status);
    }

    [Fact]
    public async Task DeletingAnotherAccountsCharacterFailsAndKeepsIt()
    {
        var store = new InMemoryCharacterStore();
        var services = SessionHarness.Services(requireRegistration: false, characterStore: store);
        await using var owner = await SessionHarness.ConnectAsync(services, Cancel);
        await using var other = await SessionHarness.ConnectAsync(services, Cancel);
        await LogInAsync(owner, "owner@example.test");
        await LogInAsync(other, "other@example.test");
        var character = await CreateAsync(owner, 0, "Kimi");

        await other.Client.SendAsync(new PqDelCharacter(0, character.CharacterId));

        Assert.Equal(
            new PrDelCharacter(DeleteCharacterStatus.Failed, 0, Catalog: null),
            await other.Client.ReceiveAsync<PrDelCharacter>(PrDelCharacter.TryRead));
        Assert.True(await store.IsNameTakenAsync("Kimi", Cancel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public async Task ADeletionForASlotTheConnectionDoesNotHoldFailsForSlotZero(byte slot)
    {
        var store = new InMemoryCharacterStore();
        await using var connection = await SessionHarness.ConnectAsync(
            SessionHarness.Services(requireRegistration: false, characterStore: store), Cancel);
        await LogInAsync(connection, "player@example.test");
        var character = await CreateAsync(connection, 0, "Kimi");

        await connection.Client.SendAsync(new PqDelCharacter(slot, character.CharacterId));

        Assert.Equal(
            new PrDelCharacter(DeleteCharacterStatus.Failed, 0, Catalog: null),
            await connection.Client.ReceiveAsync<PrDelCharacter>(PrDelCharacter.TryRead));
        Assert.True(await store.IsNameTakenAsync("Kimi", Cancel));
    }

    private static async Task<PrUserLoginSucceeded> LogInAsync(Connection connection, params string[] emailAddresses)
    {
        await connection.Client.SendAsync(new PqUserLogin(
            [.. emailAddresses.Select(address => new LoginAccountInfo(address, new Secret("digest"), 0, string.Empty))],
            new ConnectionAddress(0, 0),
            0));
        return Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
    }

    private static async Task<CharacterInfo> CreateAsync(Connection connection, byte slot, string name)
    {
        await connection.Client.SendAsync(new PqNewCharacter(slot, name, EveryPart));
        var answer = await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead);
        Assert.Equal(NewCharacterStatus.Created, answer.Status);
        return Assert.Single(answer.Catalog!.Characters);
    }
}
