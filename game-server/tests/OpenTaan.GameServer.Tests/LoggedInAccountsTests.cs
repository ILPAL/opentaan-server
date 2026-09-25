using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Tests.GameServer;

/// <summary>Which connection holds each account. Every value here is invented.</summary>
public sealed class LoggedInAccountsTests
{
    private static readonly SessionServices Services = SessionHarness.Services();

    private static readonly Account First = new(1001, 1, "first@example.test", new Secret("digest"), Gender.Male);
    private static readonly Account Second = new(1002, 2, "second@example.test", new Secret("digest"), Gender.Female);

    [Fact]
    public void AFreeAccountIsClaimedAndHeldByItsConnection()
    {
        var accounts = new LoggedInAccounts();
        var session = NewSession(1);

        Assert.Null(accounts.TryClaim(session, [First]));
        Assert.Same(session, accounts.HolderOf(First.AccountId));
    }

    [Fact]
    public void AnAccountHeldElsewhereIsRefusedAndTheHolderNamed()
    {
        var accounts = new LoggedInAccounts();
        var holder = NewSession(1);
        accounts.TryClaim(holder, [First]);

        Assert.Same(holder, accounts.TryClaim(NewSession(2), [First]));
        Assert.Same(holder, accounts.HolderOf(First.AccountId));
    }

    [Fact]
    public void ARefusedClaimTakesNoneOfItsAccounts()
    {
        var accounts = new LoggedInAccounts();
        accounts.TryClaim(NewSession(1), [Second]);

        Assert.NotNull(accounts.TryClaim(NewSession(2), [First, Second]));
        Assert.Null(accounts.HolderOf(First.AccountId));
    }

    [Fact]
    public void ReleasingFreesOnlyWhatThatConnectionHolds()
    {
        var accounts = new LoggedInAccounts();
        var holder = NewSession(1);
        var other = NewSession(2);
        accounts.TryClaim(holder, [First]);
        accounts.TryClaim(other, [Second]);

        // A connection whose claim was refused releases on close too, and must not free the holder's.
        accounts.Release(other, [First, Second]);

        Assert.Same(holder, accounts.HolderOf(First.AccountId));
        Assert.Null(accounts.HolderOf(Second.AccountId));
    }

    [Fact]
    public async Task OfManySimultaneousClaimsForOneAccountExactlyOneSucceeds()
    {
        const int Contenders = 64;
        var accounts = new LoggedInAccounts();
        var sessions = Enumerable.Range(1, Contenders).Select(id => NewSession(id)).ToArray();
        using var start = new Barrier(Contenders);

        // A thread each, not pool tasks: 64 pool threads blocked on the barrier would
        // starve every other test's timers and sockets.
        var results = await Task.WhenAll(sessions.Select(session => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken);
                return accounts.TryClaim(session, [First]);
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));

        var winner = Assert.Single(sessions, session => results[Array.IndexOf(sessions, session)] is null);
        Assert.Same(winner, accounts.HolderOf(First.AccountId));
        Assert.All(results.Where(holder => holder is not null), holder => Assert.Same(winner, holder));
    }

    private static ClientSession NewSession(long id) => new(id, null, Stream.Null, Services);
}
