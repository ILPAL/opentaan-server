using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OpenTaan.Protocol;

namespace OpenTaan.GameServer.Accounts;

/// <summary>Makes and checks <see cref="StoredPassword"/>s, a few at a time.</summary>
/// <remarks>
/// Each hash and each check takes as long as its rounds, by design, and holds a thread for
/// all of it. So at most half the processors' worth run at once, and never fewer than one;
/// the rest wait without holding a thread. A burst of logins then slows other logins
/// down, never the rest of the server. Limiting how often one account or one address may
/// try a password is a separate guard.
/// </remarks>
public sealed class PasswordHasher
{
    private readonly SemaphoreSlim _running;
    private readonly Action? _beforeEach;

    /// <summary>A hasher whose new hashes take <see cref="AccountOptions.PasswordIterations"/> rounds.</summary>
    public PasswordHasher(IOptions<AccountOptions> options)
        : this(
            (options ?? throw new ArgumentNullException(nameof(options))).Value.PasswordIterations,
            Math.Max(1, Environment.ProcessorCount / 2),
            beforeEach: null)
    {
    }

    /// <summary>A hasher with a limit and a hook of the test's choosing.</summary>
    /// <param name="iterations">The rounds each new hash takes.</param>
    /// <param name="concurrency">How many hashes and checks may run at once.</param>
    /// <param name="beforeEach">Runs as each hash or check starts, once it is let through.</param>
    internal PasswordHasher(int iterations, int concurrency, Action? beforeEach)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);

        Iterations = iterations;
        _running = new SemaphoreSlim(concurrency, concurrency);
        _beforeEach = beforeEach;
    }

    /// <summary>The rounds each new hash takes.</summary>
    public int Iterations { get; }

    /// <summary>A new hash of <paramref name="digest"/>, with a salt of its own.</summary>
    /// <param name="digest">The password as the client sent it.</param>
    /// <param name="cancellationToken">Ends the wait for a turn, as when the connection closes.</param>
    public ValueTask<StoredPassword> HashAsync(Secret digest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var salt = RandomNumberGenerator.GetBytes(StoredPassword.SaltSize);
        return RunAsync(() => StoredPassword.Derive(digest, salt, Iterations), cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="digest"/> is the password <paramref name="stored"/> was made
    /// from, checked with the rounds it was made with.
    /// </summary>
    /// <param name="stored">The account's password.</param>
    /// <param name="digest">The password as the client sent it.</param>
    /// <param name="cancellationToken">Ends the wait for a turn, as when the connection closes.</param>
    public ValueTask<bool> VerifyAsync(StoredPassword stored, Secret digest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(digest);

        return RunAsync(() => stored.Matches(digest), cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="stored"/> took fewer rounds than a new hash takes, so that a
    /// login that has just proved its password should hash it again.
    /// </summary>
    public bool NeedsRehash(StoredPassword stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return stored.Iterations < Iterations;
    }

    private async ValueTask<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await _running.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _beforeEach?.Invoke();
            return work();
        }
        finally
        {
            _running.Release();
        }
    }
}
