using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Characters;

namespace OpenTaan.GameServer.Matches;

/// <summary>Keeps what each match paid, away from the rooms' lock, and says once it is kept.</summary>
/// <remarks>
/// <para>
/// A match's end is sent at once, under the rooms' lock, with what the match pays, as the
/// client's result screen starts as soon as the end arrives. The payment is kept after, by
/// the character store, which waits on the database, so it runs on a thread of its own.
/// </para>
/// <para>
/// A try that fails is made again after <see cref="RewardOptions.PayoutRetryDelay"/>, up to
/// <see cref="RewardOptions.PayoutAttempts"/> tries in all: the store pays each character
/// once however many times a match is kept, so a try whose outcome is unknown can always
/// be made again. Once the payment is kept, the caller is told how each character stands
/// now. A payment never kept is logged as lost, with what it would have paid, and the
/// characters keep what they had.
/// </para>
/// <para>
/// When the server stops, it waits for every payment under way.
/// </para>
/// </remarks>
/// <param name="characters">Where the characters and what each match paid them are kept.</param>
/// <param name="options">The rewards, with how often and how far apart a payment is tried.</param>
/// <param name="time">The clock a retry waits on.</param>
/// <param name="logger">The log.</param>
public sealed partial class MatchPayouts(
    ICharacterStore characters,
    IOptions<RewardOptions> options,
    TimeProvider time,
    ILogger<MatchPayouts> logger) : IHostedService
{
    private readonly HashSet<Task> _running = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Starts keeping <paramref name="match"/>, and calls <paramref name="kept"/> with how each
    /// of its characters stands once it is kept. Returns at once; neither runs on the caller's thread.
    /// </summary>
    public void Pay(MatchRecord match, Action<IReadOnlyDictionary<uint, CharacterProgress>> kept)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(kept);

        var paying = Task.Run(() => PayAsync(match, kept));
        lock (_lock)
        {
            _running.Add(paying);
        }

        // Registered once the task is in the set, so that it leaves the set after it joined.
        paying.ContinueWith(
            done =>
            {
                lock (_lock)
                {
                    _running.Remove(done);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Completes once every payment under way has been kept or given up on.</summary>
    public Task WhenPaidAsync()
    {
        lock (_lock)
        {
            return Task.WhenAll([.. _running]);
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Waits for every payment under way, until the host stops waiting.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => WhenPaidAsync().WaitAsync(cancellationToken);

    private async Task PayAsync(MatchRecord match, Action<IReadOnlyDictionary<uint, CharacterProgress>> kept)
    {
        IReadOnlyDictionary<uint, CharacterProgress> progress;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                progress = await characters.KeepMatchAsync(match, CancellationToken.None).ConfigureAwait(false);
                break;
            }
            catch (Exception exception) when (attempt < options.Value.PayoutAttempts)
            {
                LogRetrying(logger, exception, match.MatchId, attempt, options.Value.PayoutRetryDelay);
                await Task.Delay(options.Value.PayoutRetryDelay, time).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogLost(logger, exception, match.MatchId, attempt, Describe(match.Payments));
                return;
            }
        }

        LogKept(logger, match.MatchId, match.RoomNumber, Describe(match.Payments));
        kept(progress);
    }

    // Each payment, for the log: enough to pay it by hand if it is lost.
    private static string Describe(IEnumerable<MatchPayment> payments) => string.Join(
        "; ",
        payments.Select(payment =>
            $"character {payment.CharacterId}: {payment.Experience} experience, {payment.Gold} gold, " +
            $"{(payment.Won ? "won" : "lost")}, {payment.Ending}"));

    [LoggerMessage(Level = LogLevel.Information, Message = "Match {MatchId} in room {RoomNumber} paid {Payments}")]
    private static partial void LogKept(ILogger logger, Guid matchId, ushort roomNumber, string payments);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Match {MatchId}'s payment failed on try {Attempt}, and is tried again in {Delay}")]
    private static partial void LogRetrying(ILogger logger, Exception exception, Guid matchId, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Match {MatchId}'s payment was lost after {Attempts} tries, and paid no one: {Payments}")]
    private static partial void LogLost(ILogger logger, Exception exception, Guid matchId, int attempts, string payments);
}
