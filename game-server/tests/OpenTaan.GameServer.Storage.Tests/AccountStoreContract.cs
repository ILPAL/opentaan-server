using OpenTaan.GameServer.Accounts;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Tests.GameServer;

namespace OpenTaan.Tests.Storage;

/// <summary>
/// What every account store must do, run against each of them. The in-memory store and
/// the PostgreSQL store pass the same tests, so the flow tests, which run on the first,
/// speak for the second. Every value here is invented.
/// </summary>
public abstract class AccountStoreContract
{
    /// <summary>The cash each store's new accounts start with.</summary>
    protected const uint StartingCash = 500;

    private const int Contenders = 64;

    /// <summary>What a client sends for a password.</summary>
    protected static readonly Secret Digest = new("5f4dcc3b5aa765d61d8327deb882cf99");

    protected static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>A store with no accounts, whose new accounts start with <see cref="StartingCash"/>.</summary>
    protected abstract Task<IAccountStore> NewStoreAsync();

    /// <summary>The accounts of <paramref name="store"/>, read afresh: through new connections, for a store that has any.</summary>
    protected abstract Task<IAccountStore> ReopenAsync(IAccountStore store);

    [Fact]
    public async Task AccountsAreNumberedFromTheFirstIdsTheReferenceSessionsRecord()
    {
        var store = await NewStoreAsync();

        var first = await store.CreateAsync("first@example.test", await PasswordAsync(), Gender.Male, Cancel);
        var second = await store.CreateAsync("second@example.test", await PasswordAsync(), Gender.Female, Cancel);

        Assert.Equal((1001u, 1u), (first!.AccountId, first.LocalPlayerId));
        Assert.Equal((1002u, 2u), (second!.AccountId, second.LocalPlayerId));
    }

    [Fact]
    public async Task AnAccountReadsBackAsItWasCreatedOnceReopened()
    {
        var store = await NewStoreAsync();
        var password = await PasswordAsync();
        var created = (await store.CreateAsync("player@example.test", password, Gender.Female, Cancel))!;

        var read = await (await ReopenAsync(store)).FindAsync("player@example.test", Cancel);

        Assert.NotNull(read);
        Assert.Equal(
            (created.AccountId, created.LocalPlayerId, "player@example.test", Gender.Female, StartingCash),
            (read.AccountId, read.LocalPlayerId, read.EmailAddress, read.Gender, read.Cash));
        Assert.Equal(password.Format(), read.Password.Format());
        Assert.True(await SessionHarness.FastPasswords.VerifyAsync(read.Password, Digest, Cancel));
    }

    [Fact]
    public async Task AnAddressNoAccountHasFindsNothing()
    {
        var store = await NewStoreAsync();
        await store.CreateAsync("player@example.test", await PasswordAsync(), Gender.Male, Cancel);

        Assert.Null(await store.FindAsync("nobody@example.test", Cancel));
    }

    [Fact]
    public async Task ATakenAddressIsRefusedAndItsAccountKeepsItsPassword()
    {
        var store = await NewStoreAsync();
        var first = await PasswordAsync();
        await store.CreateAsync("player@example.test", first, Gender.Male, Cancel);

        Assert.Null(await store.CreateAsync("player@example.test", await PasswordAsync(), Gender.Female, Cancel));

        var kept = (await store.FindAsync("player@example.test", Cancel))!;
        Assert.Equal((first.Format(), Gender.Male), (kept.Password.Format(), kept.Gender));
    }

    [Fact]
    public async Task AddressesAreComparedExactlyCaseIncluded()
    {
        var store = await NewStoreAsync();

        var upper = await store.CreateAsync("Player@example.test", await PasswordAsync(), Gender.Male, Cancel);
        var lower = await store.CreateAsync("player@example.test", await PasswordAsync(), Gender.Male, Cancel);

        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.NotEqual(upper.AccountId, lower.AccountId);
        Assert.Null(await store.FindAsync("PLAYER@example.test", Cancel));
    }

    [Fact]
    public async Task FindOrCreateCreatesOnlyAnAccountThatIsNotThere()
    {
        var store = await NewStoreAsync();
        var first = await PasswordAsync();

        var created = await store.FindOrCreateAsync("player@example.test", first, Gender.Female, Cancel);
        var found = await store.FindOrCreateAsync("player@example.test", await PasswordAsync(), Gender.Male, Cancel);

        Assert.Equal(created.AccountId, found.AccountId);
        Assert.Equal((first.Format(), Gender.Female), (found.Password.Format(), found.Gender));
    }

    [Fact]
    public async Task APasswordIsReplacedForTheAccountNamedAndNoOther()
    {
        var store = await NewStoreAsync();
        var named = (await store.CreateAsync("named@example.test", await PasswordAsync(), Gender.Male, Cancel))!;
        var other = (await store.CreateAsync("other@example.test", await PasswordAsync(), Gender.Male, Cancel))!;
        var replacement = await PasswordAsync();

        var replaced = await store.ReplacePasswordAsync(named.AccountId, replacement, Cancel);

        Assert.Equal(replacement.Format(), replaced!.Password.Format());
        Assert.Equal((named.AccountId, named.EmailAddress), (replaced.AccountId, replaced.EmailAddress));
        var reopened = await ReopenAsync(store);
        Assert.Equal(replacement.Format(), (await reopened.FindAsync("named@example.test", Cancel))!.Password.Format());
        Assert.Equal(other.Password.Format(), (await reopened.FindAsync("other@example.test", Cancel))!.Password.Format());
        Assert.Null(await store.ReplacePasswordAsync(9999, replacement, Cancel));
    }

    [Fact]
    public async Task OfManySimultaneousCreationsOfOneAddressExactlyOneSucceeds()
    {
        var store = await NewStoreAsync();
        var password = await PasswordAsync();

        var created = await Race(_ => store.CreateAsync("player@example.test", password, Gender.Male, Cancel));

        var winner = Assert.Single(created, account => account is not null);
        Assert.Equal(winner!.AccountId, (await store.FindAsync("player@example.test", Cancel))!.AccountId);
    }

    [Fact]
    public async Task SimultaneousCreationsOfManyAddressesEachTakeIdsOfTheirOwn()
    {
        var store = await NewStoreAsync();
        var password = await PasswordAsync();

        var created = await Race(contender => store.CreateAsync($"player{contender}@example.test", password, Gender.Male, Cancel));

        Assert.All(created, Assert.NotNull);
        Assert.Equal(Contenders, created.Select(account => account!.AccountId).Distinct().Count());
        Assert.Equal(Contenders, created.Select(account => account!.LocalPlayerId).Distinct().Count());
    }

    /// <summary>A hash of <see cref="Digest"/>, made as quickly as the tests allow.</summary>
    protected static async Task<StoredPassword> PasswordAsync() =>
        await SessionHarness.FastPasswords.HashAsync(Digest, Cancel);

    // Runs every contender at once, each on a thread of its own: 64 pool threads
    // blocked on the barrier would starve every other test's timers and sockets.
    private static async Task<Account?[]> Race(Func<int, ValueTask<Account?>> create)
    {
        using var start = new Barrier(Contenders);
        return await Task.WhenAll(Enumerable.Range(1, Contenders).Select(contender => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(Cancel);
                return create(contender).AsTask().GetAwaiter().GetResult();
            },
            Cancel,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));
    }
}
