using OpenTaan.GameServer.Characters;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Tests.GameServer;

namespace OpenTaan.Tests.Storage;

/// <summary>
/// What every character store must do, run against each of them: ids, names, one
/// character per account, the finished tutorial, and creations that race. The in-memory
/// store and the PostgreSQL store pass the same tests, so the flow tests, which run on the
/// first, speak for the second. Every value here is invented.
/// </summary>
public abstract class CharacterStoreContract
{
    private const int Contenders = 64;

    protected static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>The experience at which level 2 begins, as the stores' settings have it.</summary>
    protected static uint ExperienceCeiling => SessionHarness.ShippedCharacterOptions().Start.ExperienceCeiling;

    /// <summary>A store with no characters, whose experience bars come from the shipped settings.</summary>
    protected abstract Task<ICharacterStore> NewStoreAsync();

    /// <summary>The characters of <paramref name="store"/>, read afresh: through new connections, for a store that has any.</summary>
    protected abstract Task<ICharacterStore> ReopenAsync(ICharacterStore store);

    /// <summary>Makes male accounts with these ids exist, for a store that keeps characters with their accounts.</summary>
    protected abstract Task GivenAccountsAsync(params uint[] accountIds);

    [Fact]
    public async Task CharactersAreNumberedFromTheFirstIdTheReferenceSessionsRecord()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);

        var first = await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);
        var second = await store.CreateAsync(1002, "Rồng", Builder(1002, "Rồng"), Cancel);

        Assert.Equal(1001u, first.Character!.CharacterId);
        Assert.Equal(1002u, second.Character!.CharacterId);
        Assert.Equal(first.Character, Assert.Single(await store.FindByAccountAsync(1001, Cancel)));
    }

    [Fact]
    public async Task ACharacterReadsBackExactlyAsItWasBuiltOnceReopened()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        AppearanceSlot[] parts = new AppearanceSlot[AppearanceSlot.Count];
        parts[1] = new AppearanceSlot(110_001, 3, 0, 7);
        parts[3] = new AppearanceSlot(130_002, 0, 0, 0);
        parts[8] = new AppearanceSlot(0, 9, 0, 0);
        parts[AppearanceSlot.Count - 1] = new AppearanceSlot(uint.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

        var created = (await store.CreateAsync(1001, "Kimi", id => NewCharacter(id, 1001, "Kimi", parts), Cancel)).Character!;

        Assert.Equal(created, Assert.Single(await (await ReopenAsync(store)).FindByAccountAsync(1001, Cancel)));
    }

    [Fact]
    public async Task AFinishedTutorialIsNewsOnceAndStaysFinishedOnceReopened()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        var created = (await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel)).Character!;
        Assert.False(created.HasFinishedTutorial);

        Assert.True(await store.FinishTutorialAsync(created.CharacterId, Cancel));
        Assert.False(await store.FinishTutorialAsync(created.CharacterId, Cancel));
        Assert.False(await store.FinishTutorialAsync(created.CharacterId + 1, Cancel));

        var reopened = await ReopenAsync(store);
        Assert.Equal(created with { HasFinishedTutorial = true }, Assert.Single(await reopened.FindByAccountAsync(1001, Cancel)));
        Assert.False(await reopened.FinishTutorialAsync(created.CharacterId, Cancel));
    }

    [Fact]
    public async Task AFinishedTutorialIsKeptForThatCharacterAlone()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);
        var finished = (await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel)).Character!;
        await store.CreateAsync(1002, "Rồng", Builder(1002, "Rồng"), Cancel);

        await store.FinishTutorialAsync(finished.CharacterId, Cancel);

        Assert.False(Assert.Single(await store.FindByAccountAsync(1002, Cancel)).HasFinishedTutorial);
    }

    [Fact]
    public async Task ANameDifferingOnlyInCaseIsTaken()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);
        await store.CreateAsync(1001, "Rồng", Builder(1001, "Rồng"), Cancel);

        var again = await store.CreateAsync(1002, "RỒNG", Builder(1002, "RỒNG"), Cancel);

        Assert.Equal(new CharacterCreation(CharacterCreationOutcome.NameTaken, null), again);
        Assert.True(await store.IsNameTakenAsync("rỒng", Cancel));
        Assert.Empty(await store.FindByAccountAsync(1002, Cancel));
    }

    [Fact]
    public async Task ANameWithTheLongSIsTakenByTheNameWithS()
    {
        // The letter on which .NET's case-insensitive comparison and its capitals disagree.
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);
        await store.CreateAsync(1001, "Kisi", Builder(1001, "Kisi"), Cancel);

        var again = await store.CreateAsync(1002, "Kiſi", Builder(1002, "Kiſi"), Cancel);

        Assert.Equal(CharacterCreationOutcome.NameTaken, again.Outcome);
        Assert.True(await store.IsNameTakenAsync("KIſI", Cancel));
    }

    [Fact]
    public async Task AnAccountHasOneCharacter()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);

        var second = await store.CreateAsync(1001, "Rồng", Builder(1001, "Rồng"), Cancel);

        Assert.Equal(new CharacterCreation(CharacterCreationOutcome.AccountHasCharacter, null), second);
        Assert.False(await store.IsNameTakenAsync("Rồng", Cancel));
    }

    [Fact]
    public async Task AnAccountWithACharacterIsToldSoEvenForANameThatIsTaken()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);
        await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);
        await store.CreateAsync(1002, "Rồng", Builder(1002, "Rồng"), Cancel);

        var again = await store.CreateAsync(1001, "Rồng", Builder(1001, "Rồng"), Cancel);

        Assert.Equal(CharacterCreationOutcome.AccountHasCharacter, again.Outcome);
    }

    [Fact]
    public async Task DeletingACharacterFreesItsNameAndItsAccount()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        var created = await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);

        Assert.True(await store.DeleteAsync(1001, created.Character!.CharacterId, Cancel));

        Assert.False(await store.IsNameTakenAsync("Kimi", Cancel));
        Assert.Empty(await store.FindByAccountAsync(1001, Cancel));
        Assert.Equal(
            CharacterCreationOutcome.Created,
            (await store.CreateAsync(1001, "KIMI", Builder(1001, "KIMI"), Cancel)).Outcome);
    }

    [Fact]
    public async Task OnlyTheOwningAccountCanDeleteACharacter()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);
        var created = await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);

        Assert.False(await store.DeleteAsync(1002, created.Character!.CharacterId, Cancel));
        Assert.False(await store.DeleteAsync(1001, created.Character.CharacterId + 1, Cancel));

        Assert.Equal(created.Character, Assert.Single(await store.FindByAccountAsync(1001, Cancel)));
    }

    [Fact]
    public async Task ACharacterBuiltUnderAnotherIdAccountOrNameIsRefused()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001, 1002);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.CreateAsync(1001, "Kimi", _ => NewCharacter(9999, 1001, "Kimi"), Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.CreateAsync(1001, "Kimi", id => NewCharacter(id, 1002, "Kimi"), Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.CreateAsync(1001, "Kimi", id => NewCharacter(id, 1001, "Kimo"), Cancel));

        Assert.False(await store.IsNameTakenAsync("Kimi", Cancel));
        Assert.Empty(await store.FindByAccountAsync(1001, Cancel));
    }

    [Fact]
    public async Task OfManySimultaneousCreationsWithOneNameExactlyOneSucceeds()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync([.. Enumerable.Range(1, Contenders).Select(contender => (uint)(1000 + contender))]);

        var outcomes = await Race(account => store.CreateAsync(account, "Kimi", Builder(account, "Kimi"), Cancel));

        Assert.Single(outcomes, outcome => outcome == CharacterCreationOutcome.Created);
        Assert.All(
            outcomes.Where(outcome => outcome != CharacterCreationOutcome.Created),
            outcome => Assert.Equal(CharacterCreationOutcome.NameTaken, outcome));
    }

    [Fact]
    public async Task OfManySimultaneousCreationsForOneAccountExactlyOneSucceeds()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);

        var outcomes = await Race(contender => store.CreateAsync(1001, $"Kim{contender:D4}", Builder(1001, $"Kim{contender:D4}"), Cancel));

        Assert.Single(outcomes, outcome => outcome == CharacterCreationOutcome.Created);
        Assert.All(
            outcomes.Where(outcome => outcome != CharacterCreationOutcome.Created),
            outcome => Assert.Equal(CharacterCreationOutcome.AccountHasCharacter, outcome));
        Assert.Single(await store.FindByAccountAsync(1001, Cancel));
    }

    /// <summary>Builds a character as creation builds one, for a male account.</summary>
    protected static Func<uint, Character> Builder(uint accountId, string name) => id => NewCharacter(id, accountId, name);

    /// <summary>A character as creation builds one: at the first level, for a male account, in no guild.</summary>
    protected static Character NewCharacter(uint characterId, uint accountId, string name, AppearanceSlot[]? parts = null) => new(
        accountId,
        new CharacterInfo(characterId, name, 1, 0, 0, ExperienceCeiling, 0, Gender.Male, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty),
        Character.AppearanceOf(Gender.Male, characterId, 1, parts ?? new AppearanceSlot[AppearanceSlot.Count]));

    // Runs every contender at once, each on a thread of its own: 64 pool threads
    // blocked on the barrier would starve every other test's timers and sockets.
    private static async Task<CharacterCreationOutcome[]> Race(Func<uint, ValueTask<CharacterCreation>> create)
    {
        using var start = new Barrier(Contenders);
        return await Task.WhenAll(Enumerable.Range(1, Contenders).Select(contender => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(Cancel);
                return create((uint)(1000 + contender)).AsTask().GetAwaiter().GetResult().Outcome;
            },
            Cancel,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));
    }
}
