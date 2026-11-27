using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Tests.GameServer;

namespace OpenTaan.Tests.Storage;

/// <summary>
/// What every character store must do, run against each of them: ids, names, one
/// character per account, the finished tutorial, creations that race, and what a match
/// pays, once, however often and however many at once keep it. The in-memory
/// store and the PostgreSQL store pass the same tests, so the flow tests, which run on the
/// first, speak for the second. Every value here is invented.
/// </summary>
public abstract class CharacterStoreContract
{
    private const int Contenders = 64;

    protected static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>The experience at which level 2 begins, as the reference sessions record a new character's bar.</summary>
    protected const uint ExperienceCeiling = 300;

    /// <summary>A store with no characters.</summary>
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

        var outcomes = await Race(async account => (await store.CreateAsync(account, "Kimi", Builder(account, "Kimi"), Cancel)).Outcome);

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

        var outcomes = await Race(async contender => (await store.CreateAsync(1001, $"Kim{contender:D4}", Builder(1001, $"Kim{contender:D4}"), Cancel)).Outcome);

        Assert.Single(outcomes, outcome => outcome == CharacterCreationOutcome.Created);
        Assert.All(
            outcomes.Where(outcome => outcome != CharacterCreationOutcome.Created),
            outcome => Assert.Equal(CharacterCreationOutcome.AccountHasCharacter, outcome));
        Assert.Single(await store.FindByAccountAsync(1001, Cancel));
    }

    [Fact]
    public async Task AMatchPaysEachCharacterItsExperienceAndGoldAndCountsItsResultOnceReopened()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);

        var kept = await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180), Paid(rong, 2, won: false, 40, 120)), Cancel);

        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), kept[kimi]);
        Assert.Equal(new CharacterProgress(1, 40, 120, 0, 1), kept[rong]);
        var reopened = await ReopenAsync(store);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), CharacterProgress.Of((await reopened.FindByAccountAsync(1001, Cancel))[0].Info));
        Assert.Equal(new CharacterProgress(1, 40, 120, 0, 1), CharacterProgress.Of((await reopened.FindByAccountAsync(1002, Cancel))[0].Info));
    }

    [Fact]
    public async Task ACharacterPaidPastALevelsEndIsAtTheNextLevelWithItsBarAndAppearance()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);

        await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 250, 0)), Cancel);
        await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 0)), Cancel);

        var character = (await (await ReopenAsync(store)).FindByAccountAsync(1001, Cancel))[0];
        Assert.Equal((2, 310u, 300u, 791u), (character.Info.Level, character.Info.CurrentExperience, character.Info.ExperienceFloor, character.Info.ExperienceCeiling));
        Assert.Equal((ushort)2, character.Appearance.Field0C);
    }

    [Fact]
    public async Task KeepingAMatchAgainPaysNoOneTwiceAndTellsHowEachStands()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180), Paid(rong, 2, won: false, 40, 120));

        await store.KeepMatchAsync(match, Cancel);
        var again = await store.KeepMatchAsync(match, Cancel);

        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), again[kimi]);
        Assert.Equal(new CharacterProgress(1, 40, 120, 0, 1), again[rong]);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), CharacterProgress.Of((await (await ReopenAsync(store)).FindByAccountAsync(1001, Cancel))[0].Info));
    }

    [Fact]
    public async Task AnotherMatchPaysTheSameCharactersAgain()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);

        await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180)), Cancel);
        var kept = await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: false, 40, 120)), Cancel);

        Assert.Equal(new CharacterProgress(1, 100, 300, 1, 1), kept[kimi]);
    }

    [Fact]
    public async Task OfManySimultaneousKeepingsOfOneMatchEachCharacterIsPaidOnce()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180), Paid(rong, 2, won: false, 40, 120));

        var told = await Race(async _ => await store.KeepMatchAsync(match, Cancel));

        Assert.All(told, kept => Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), kept[kimi]));
        Assert.All(told, kept => Assert.Equal(new CharacterProgress(1, 40, 120, 0, 1), kept[rong]));
        var reopened = await ReopenAsync(store);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), CharacterProgress.Of((await reopened.FindByAccountAsync(1001, Cancel))[0].Info));
        Assert.Equal(new CharacterProgress(1, 40, 120, 0, 1), CharacterProgress.Of((await reopened.FindByAccountAsync(1002, Cancel))[0].Info));
    }

    [Fact]
    public async Task OfManyMatchesKeptAtOncePayingOneCharacterEveryPayIsAddedToTheRest()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);

        await Race(async _ => await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 1, 3)), Cancel));

        Assert.Equal(
            new CharacterProgress(1, Contenders, 3 * Contenders, Contenders, 0),
            CharacterProgress.Of((await (await ReopenAsync(store)).FindByAccountAsync(1001, Cancel))[0].Info));
    }

    [Fact]
    public async Task ACharacterDeletedBeforeItsMatchIsKeptIsLeftOutAndTheOthersArePaid()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);
        Assert.True(await store.DeleteAsync(1002, rong, Cancel));

        var kept = await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180), Paid(rong, 2, won: false, 40, 120)), Cancel);

        Assert.Equal([kimi], kept.Keys);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), kept[kimi]);
    }

    [Fact]
    public async Task CardsPayTheCharacterTheyNameOnceWithoutCountingAnotherMatch()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180), Paid(rong, 2, won: false, 40, 120)) with
        {
            Cards =
            [
                new MatchCardOffer(kimi, 0, CardRewardKind.Experience, 72),
                new MatchCardOffer(rong, 2, CardRewardKind.Gold, 50),
            ],
        };
        await store.KeepMatchAsync(match, Cancel);

        Assert.Equal(new CharacterProgress(1, 132, 180, 1, 0), await store.KeepCardAsync(match.MatchId, kimi, 5, Cancel));
        Assert.Equal(new CharacterProgress(1, 40, 170, 0, 1), await store.KeepCardAsync(match.MatchId, rong, 2, Cancel));

        var reopened = await ReopenAsync(store);
        Assert.Equal(new CharacterProgress(1, 132, 180, 1, 0), CharacterProgress.Of(Assert.Single(await reopened.FindByAccountAsync(1001, Cancel)).Info));
        Assert.Equal(new CharacterProgress(1, 40, 170, 0, 1), CharacterProgress.Of(Assert.Single(await reopened.FindByAccountAsync(1002, Cancel)).Info));
        Assert.Equal(new CharacterProgress(1, 132, 180, 1, 0), await reopened.KeepCardAsync(match.MatchId, kimi, 5, Cancel));
        Assert.Equal(new CharacterProgress(1, 40, 170, 0, 1), await reopened.KeepCardAsync(match.MatchId, rong, 2, Cancel));
    }

    [Fact]
    public async Task ACardCannotBePaidBeforeItsMatchOrTakenFromAnotherCharacter()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180), Paid(rong, 2, won: false, 40, 120)) with
        {
            Cards =
            [
                new MatchCardOffer(kimi, 0, CardRewardKind.Gold, 72),
                new MatchCardOffer(rong, 2, CardRewardKind.Experience, 20),
            ],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.KeepCardAsync(match.MatchId, kimi, 4, Cancel));
        await store.KeepMatchAsync(match, Cancel);
        Assert.Equal(new CharacterProgress(1, 60, 252, 1, 0), await store.KeepCardAsync(match.MatchId, kimi, 4, Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.KeepCardAsync(match.MatchId, rong, 4, Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.KeepCardAsync(match.MatchId, kimi, 6, Cancel));
        Assert.Equal(new CharacterProgress(1, 60, 252, 1, 0), CharacterProgress.Of(Assert.Single(await store.FindByAccountAsync(1001, Cancel)).Info));
        Assert.Equal(new CharacterProgress(1, 40, 120, 0, 1), CharacterProgress.Of(Assert.Single(await store.FindByAccountAsync(1002, Cancel)).Info));
    }

    [Fact]
    public async Task OfManySimultaneousKeepingsOfOneCardOnlyOneAmountIsAdded()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180)) with
        {
            Cards = [new MatchCardOffer(kimi, 0, CardRewardKind.Gold, 50)],
        };
        await store.KeepMatchAsync(match, Cancel);

        var told = await Race(async _ => await store.KeepCardAsync(match.MatchId, kimi, 3, Cancel));

        Assert.All(told, progress => Assert.Equal(new CharacterProgress(1, 60, 230, 1, 0), progress));
        Assert.Equal(new CharacterProgress(1, 60, 230, 1, 0), CharacterProgress.Of(Assert.Single(await (await ReopenAsync(store)).FindByAccountAsync(1001, Cancel)).Info));
    }

    [Fact]
    public async Task ACardForADeletedCharacterStaysUnpaid()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180)) with
        {
            Cards = [new MatchCardOffer(kimi, 0, CardRewardKind.Experience, 72)],
        };
        await store.KeepMatchAsync(match, Cancel);
        Assert.True(await store.DeleteAsync(1001, kimi, Cancel));

        Assert.Null(await store.KeepCardAsync(match.MatchId, kimi, 3, Cancel));
        Assert.Null(await (await ReopenAsync(store)).KeepCardAsync(match.MatchId, kimi, 3, Cancel));
    }

    [Fact]
    public async Task CardExperienceStopsAtTheLevelTablesLastValue()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);
        var match = MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, LevelTable.MostExperience - 10, 0)) with
        {
            Cards = [new MatchCardOffer(kimi, 0, CardRewardKind.Experience, 360)],
        };
        await store.KeepMatchAsync(match, Cancel);

        var progress = await store.KeepCardAsync(match.MatchId, kimi, 7, Cancel);

        Assert.Equal(LevelTable.MostExperience, progress?.Experience);
        Assert.Equal(LevelTable.LastLevel, progress?.Level);
        Assert.Equal(1u, progress?.Wins);
        Assert.Equal(0u, progress?.Losses);
    }

    /// <summary>Accounts 1001 and 1002, each with a character as creation builds one, Kimi and Rồng; returns their ids.</summary>
    protected async Task<(uint Kimi, uint Rong)> TwoCharactersAsync(ICharacterStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        await GivenAccountsAsync(1001, 1002);
        var kimi = (await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel)).Character!.CharacterId;
        var rong = (await store.CreateAsync(1002, "Rồng", Builder(1002, "Rồng"), Cancel)).Character!.CharacterId;
        return (kimi, rong);
    }

    /// <summary>A match between teams, ended, with <paramref name="payments"/>; team 1 won.</summary>
    protected static MatchRecord MatchOf(Guid matchId, params MatchPayment[] payments) => new(
        matchId,
        RoomNumber: 7,
        ChannelId: 11,
        MapId: 3,
        MissionDifficulty: null,
        WinningTeam: 1,
        Started: new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero),
        Ended: new DateTimeOffset(2026, 11, 27, 9, 12, 0, TimeSpan.Zero),
        payments);

    /// <summary>A payment to a character that played its match to the end.</summary>
    protected static MatchPayment Paid(uint characterId, byte slot, bool won, uint experience, uint gold) =>
        new(characterId, slot, MatchEnding.Played, won, experience, gold);

    /// <summary>Builds a character as creation builds one, for a male account.</summary>
    protected static Func<uint, Character> Builder(uint accountId, string name) => id => NewCharacter(id, accountId, name);

    /// <summary>A character as creation builds one: at the first level, for a male account, in no guild.</summary>
    protected static Character NewCharacter(uint characterId, uint accountId, string name, AppearanceSlot[]? parts = null) => new(
        accountId,
        new CharacterInfo(characterId, name, 1, 0, 0, ExperienceCeiling, 0, Gender.Male, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty),
        Character.AppearanceOf(Gender.Male, characterId, 1, parts ?? new AppearanceSlot[AppearanceSlot.Count]));

    // Runs every contender at once, each on a thread of its own: 64 pool threads
    // blocked on the barrier would starve every other test's timers and sockets.
    private static async Task<T[]> Race<T>(Func<uint, Task<T>> run)
    {
        using var start = new Barrier(Contenders);
        return await Task.WhenAll(Enumerable.Range(1, Contenders).Select(contender => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(Cancel);
                return run((uint)(1000 + contender)).GetAwaiter().GetResult();
            },
            Cancel,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));
    }
}
