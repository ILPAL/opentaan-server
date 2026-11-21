using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Accounts;
using OpenTaan.Protocol;

namespace OpenTaan.Tests.GameServer;

/// <summary>Making and checking password hashes: the rounds they take, and how many run at once.</summary>
public sealed class PasswordHasherTests
{
    private static readonly Secret Digest = new("5f4dcc3b5aa765d61d8327deb882cf99");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewHashTakesTheRoundsTheSettingAsksFor()
    {
        var hasher = Hasher(1_500);

        var stored = await hasher.HashAsync(Digest, Cancel);

        Assert.Equal(1_500, stored.Iterations);
        Assert.True(await hasher.VerifyAsync(stored, Digest, Cancel));
    }

    [Fact]
    public async Task TheSameDigestIsHashedDifferentlyEachTime()
    {
        var hasher = Hasher(1_000);

        var first = await hasher.HashAsync(Digest, Cancel);
        var second = await hasher.HashAsync(Digest, Cancel);

        Assert.NotEqual(first.Format(), second.Format());
        Assert.True(await hasher.VerifyAsync(first, Digest, Cancel));
        Assert.True(await hasher.VerifyAsync(second, Digest, Cancel));
    }

    [Fact]
    public async Task AHashMadeBeforeTheSettingWasRaisedStillMatchesWithTheRoundsItWasMadeWith()
    {
        var stored = await Hasher(1_000).HashAsync(Digest, Cancel);
        var raised = Hasher(2_000);

        Assert.True(await raised.VerifyAsync(stored, Digest, Cancel));
        Assert.False(await raised.VerifyAsync(stored, new Secret("5f4dcc3b5aa765d61d8327deb882cf98"), Cancel));
    }

    [Theory]
    [InlineData(1_000, true)]
    [InlineData(2_000, false)]
    [InlineData(3_000, false)]
    public async Task OnlyAHashWithFewerRoundsThanTheSettingIsMadeAgain(int iterations, bool remade)
    {
        var stored = await Hasher(iterations).HashAsync(Digest, Cancel);

        Assert.Equal(remade, Hasher(2_000).NeedsRehash(stored));
    }

    [Fact]
    public async Task NoMoreHashesOrChecksRunAtOnceThanTheLimit()
    {
        var stored = await Hasher(1_000).HashAsync(Digest, Cancel);
        using var release = new ManualResetEventSlim();
        using var twoIn = new CountdownEvent(2);
        var started = 0;
        var running = 0;
        var most = 0;
        var hasher = new PasswordHasher(1_000, concurrency: 2, () =>
        {
            Interlocked.Increment(ref started);
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            twoIn.Signal();
            release.Wait(Patience);
            Interlocked.Decrement(ref running);
        });

        Task[] work =
        [
            Task.Run(async () => await hasher.HashAsync(Digest, Cancel), Cancel),
            Task.Run(async () => await hasher.VerifyAsync(stored, Digest, Cancel), Cancel),
            Task.Run(async () => await hasher.HashAsync(Digest, Cancel), Cancel),
            Task.Run(async () => await hasher.VerifyAsync(stored, Digest, Cancel), Cancel),
        ];

        // Two are let in and held; the other two must wait for them, however long.
        Assert.True(twoIn.Wait(Patience, Cancel));
        await Task.Delay(TimeSpan.FromMilliseconds(100), Cancel);
        Assert.Equal(2, Volatile.Read(ref started));

        twoIn.Reset(2);
        release.Set();
        await Task.WhenAll(work);

        Assert.Equal(4, Volatile.Read(ref started));
        Assert.Equal(2, Volatile.Read(ref most));
    }

    [Fact]
    public async Task AWaitForATurnEndsWhenItsConnectionCloses()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var hasher = new PasswordHasher(1_000, concurrency: 1, () =>
        {
            entered.Set();
            release.Wait(Patience);
        });

        var holder = Task.Run(async () => await hasher.HashAsync(Digest, Cancel), Cancel);
        Assert.True(entered.Wait(Patience, Cancel));

        using var closing = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var waiting = hasher.HashAsync(Digest, closing.Token).AsTask();
        Assert.False(waiting.IsCompleted);

        await closing.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        release.Set();
        await holder;
    }

    private static PasswordHasher Hasher(int iterations) =>
        new(Options.Create(new AccountOptions { PasswordIterations = iterations }));

    private static void InterlockedMax(ref int target, int value)
    {
        var seen = Volatile.Read(ref target);
        while (value > seen)
        {
            var before = Interlocked.CompareExchange(ref target, value, seen);
            if (before == seen)
            {
                return;
            }

            seen = before;
        }
    }
}
