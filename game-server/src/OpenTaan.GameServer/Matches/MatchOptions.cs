using System.Globalization;
using Microsoft.Extensions.Options;

namespace OpenTaan.GameServer.Matches;

/// <summary>Settings bound from the <c>Matches</c> configuration section.</summary>
/// <remarks>
/// Each is how long the server waits on players before it goes on without them, so that
/// one client that stops answering cannot hold up everyone else.
/// </remarks>
public sealed class MatchOptions
{
    public const string Section = "Matches";

    /// <summary>
    /// How long a turn waits for its character to shoot or pass before the server passes it.
    /// The client passes for its player once its own turn timer, <see cref="MatchRules.ClientTurnLength"/>,
    /// runs out, so this is only for a client that has stopped answering, and must be longer:
    /// a turn cut while its client still counts would leave a last-second shot on its
    /// shooter's screen alone.
    /// </summary>
    public TimeSpan TurnWait { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How long a shot waits, once the first client has played it back, for every other
    /// client to finish. They play it from the moment it reaches them, so the others finish
    /// within about the time a packet takes there and back.
    /// </summary>
    public TimeSpan ShotWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a shot may wait in all, from the moment it arrives, for clients to play it
    /// back, in case none reports it finished.
    /// </summary>
    public TimeSpan LongestShot { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a mission level's first turn waits for every client to report that the
    /// level's opening is over. The opening takes about two and a half seconds; a turn
    /// started while a client is still in it leaves that client without the level's
    /// enemies, so the wait is generous.
    /// </summary>
    public TimeSpan LevelReadyWait { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long after a match's end its room waits for every player to pick a result card
    /// before it is open to new players again. The client leaves the result screen by
    /// itself about nineteen seconds after the end.
    /// </summary>
    public TimeSpan ResultWait { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>What is wrong with the waits, for the message the server refuses to start with; null when nothing is.</summary>
    public string? WaitsProblem()
    {
        if (new[] { LevelReadyWait, TurnWait, ShotWait, LongestShot, ResultWait }.Any(wait => wait <= TimeSpan.Zero))
        {
            return $"Every wait in {Section} must be positive";
        }

        return TurnWait > MatchRules.ClientTurnLength
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{Section}:{nameof(TurnWait)} must be longer than the client's own turn, {MatchRules.ClientTurnLength.TotalSeconds} seconds, or it cuts turns the client still counts");
    }
}

/// <summary>Refuses match settings whose waits <see cref="MatchOptions.WaitsProblem"/> finds wrong, naming what is wrong.</summary>
public sealed class MatchWaitsValidation : IValidateOptions<MatchOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, MatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.WaitsProblem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }
}
