using System.Globalization;
using Microsoft.Extensions.Options;

namespace OpenTaan.GameServer.Matches;

/// <summary>Settings bound from the <c>Matches</c> configuration section.</summary>
/// <remarks>
/// The waits are how long the server waits on players before it goes on without them, so
/// that one client that stops answering cannot hold up everyone else. The item settings are
/// what the start of every turn tells the clients.
/// </remarks>
public sealed class MatchOptions
{
    public const string Section = "Matches";

    /// <summary>The highest drop quality the client knows: it holds anything higher to this.</summary>
    public const uint MaxDropQuality = 100;

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

    /// <summary>
    /// How many item boxes the shot of each turn may drop. The shooter's client places them
    /// along its missiles' paths, and the client that catches one draws its item; 0 lets no
    /// box drop.
    /// </summary>
    public byte ItemBoxesPerTurn { get; set; } = 1;

    /// <summary>
    /// How good the items the clients draw are, from 0 to 100: higher makes the rarer
    /// items likelier. The reference sessions carry 20.
    /// </summary>
    public uint ItemDropQuality { get; set; } = 20;

    /// <summary>What is wrong with the settings, for the message the server refuses to start with; null when nothing is.</summary>
    public string? Problem()
    {
        if (new[] { LevelReadyWait, TurnWait, ShotWait, LongestShot, ResultWait }.Any(wait => wait <= TimeSpan.Zero))
        {
            return $"Every wait in {Section} must be positive";
        }

        if (TurnWait <= MatchRules.ClientTurnLength)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Section}:{nameof(TurnWait)} must be longer than the client's own turn, {MatchRules.ClientTurnLength.TotalSeconds} seconds, or it cuts turns the client still counts");
        }

        return ItemDropQuality <= MaxDropQuality
            ? null
            : $"{Section}:{nameof(ItemDropQuality)} must be from 0 to {MaxDropQuality}";
    }
}

/// <summary>Refuses match settings <see cref="MatchOptions.Problem"/> finds wrong, naming what is wrong.</summary>
public sealed class MatchOptionsValidation : IValidateOptions<MatchOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, MatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Problem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }
}
