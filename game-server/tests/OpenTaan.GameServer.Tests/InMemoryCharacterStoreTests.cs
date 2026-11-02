using OpenTaan.GameServer.Characters;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>Where characters are kept: ids, names, one per account, and creations that race.</summary>
public sealed class InMemoryCharacterStoreTests
{
    private const int Contenders = 64;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CharactersAreNumberedFromTheFirstIdTheReferenceSessionsRecord()
    {
        var store = new InMemoryCharacterStore();

        var first = await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);
        var second = await store.CreateAsync(1002, "Rồng", Builder(1002, "Rồng"), Cancel);

        Assert.Equal(1001u, first.Character!.CharacterId);
        Assert.Equal(1002u, second.Character!.CharacterId);
        Assert.Equal(first.Character, Assert.Single(await store.FindByAccountAsync(1001, Cancel)));
    }

    [Fact]
    public async Task ANameDifferingOnlyInCaseIsTaken()
    {
        var store = new InMemoryCharacterStore();
        await store.CreateAsync(1001, "Rồng", Builder(1001, "Rồng"), Cancel);

        var again = await store.CreateAsync(1002, "RỒNG", Builder(1002, "RỒNG"), Cancel);

        Assert.Equal(new CharacterCreation(CharacterCreationOutcome.NameTaken, null), again);
        Assert.True(await store.IsNameTakenAsync("rỒng", Cancel));
        Assert.Empty(await store.FindByAccountAsync(1002, Cancel));
    }

    [Fact]
    public async Task AnAccountHasOneCharacter()
    {
        var store = new InMemoryCharacterStore();
        await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);

        var second = await store.CreateAsync(1001, "Rồng", Builder(1001, "Rồng"), Cancel);

        Assert.Equal(new CharacterCreation(CharacterCreationOutcome.AccountHasCharacter, null), second);
        Assert.False(await store.IsNameTakenAsync("Rồng", Cancel));
    }

    [Fact]
    public async Task DeletingACharacterFreesItsNameAndItsAccount()
    {
        var store = new InMemoryCharacterStore();
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
        var store = new InMemoryCharacterStore();
        var created = await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);

        Assert.False(await store.DeleteAsync(1002, created.Character!.CharacterId, Cancel));
        Assert.False(await store.DeleteAsync(1001, created.Character.CharacterId + 1, Cancel));

        Assert.Equal(created.Character, Assert.Single(await store.FindByAccountAsync(1001, Cancel)));
    }

    [Fact]
    public async Task ACharacterBuiltUnderAnotherIdAccountOrNameIsRefused()
    {
        var store = new InMemoryCharacterStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.CreateAsync(1001, "Kimi", _ => NewCharacter(9999, 1001, "Kimi"), Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.CreateAsync(1001, "Kimi", id => NewCharacter(id, 1002, "Kimi"), Cancel));

        Assert.False(await store.IsNameTakenAsync("Kimi", Cancel));
    }

    [Fact]
    public async Task OfManySimultaneousCreationsWithOneNameExactlyOneSucceeds()
    {
        var store = new InMemoryCharacterStore();

        var outcomes = await Race(account => store.CreateAsync(account, "Kimi", Builder(account, "Kimi"), Cancel));

        Assert.Single(outcomes, outcome => outcome == CharacterCreationOutcome.Created);
        Assert.All(outcomes.Where(outcome => outcome != CharacterCreationOutcome.Created), outcome => Assert.Equal(CharacterCreationOutcome.NameTaken, outcome));
    }

    [Fact]
    public async Task OfManySimultaneousCreationsForOneAccountExactlyOneSucceeds()
    {
        var store = new InMemoryCharacterStore();

        var outcomes = await Race(contender => store.CreateAsync(1001, $"Kim{contender:D4}", Builder(1001, $"Kim{contender:D4}"), Cancel));

        Assert.Single(outcomes, outcome => outcome == CharacterCreationOutcome.Created);
        Assert.Single(await store.FindByAccountAsync(1001, Cancel));
    }

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

    private static Func<uint, Character> Builder(uint accountId, string name) => id => NewCharacter(id, accountId, name);

    private static Character NewCharacter(uint characterId, uint accountId, string name) => new(
        accountId,
        new CharacterInfo(characterId, name, 1, 0, 0, 300, 0, Gender.Male, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty),
        new CharacterAppear(Gender.Male, characterId, 1, new AppearanceSlot[AppearanceSlot.Count]));
}
