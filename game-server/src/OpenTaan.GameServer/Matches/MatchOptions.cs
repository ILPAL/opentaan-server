using System.Globalization;
using Microsoft.Extensions.Options;

namespace OpenTaan.GameServer.Matches;

/// <summary>Settings bound from the <c>Matches</c> configuration section.</summary>
/// <remarks>
/// The waits are how long the server waits on players before it goes on without them, so
/// that one client that stops answering cannot hold up everyone else. The item settings are
/// what the start of every turn tells the clients, and, between teams, how the server places
/// boxes of its own and draws their items.
/// </remarks>
public sealed class MatchOptions
{
    public const string Section = "Matches";

    /// <summary>The highest drop quality the client knows: it holds anything higher to this.</summary>
    public const uint MaxDropQuality = 100;

    /// <summary>The greatest height above a character an item box may be placed at: about the height of a whole map.</summary>
    public const int MaxItemBoxHeight = 1000;

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
    /// How long the first turn of a match between teams waits, once the first client has
    /// reported the match loaded, for every other client to report it too. Every client
    /// shows the same characters, at the same pace, before it reports, so the others report
    /// within about the time their maps take to load.
    /// </summary>
    public TimeSpan LoadWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the first turn of a match between teams may wait in all, from the match's
    /// start, in case no client reports it loaded. A client reports only once its tour of
    /// the characters is over, <see cref="MatchRules.LongestClientTour"/> in a full room, so
    /// this must be longer.
    /// </summary>
    public TimeSpan LongestLoad { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long after a match's end its room waits for every player to pick a result card
    /// before it is open to new players again. The client leaves the result screen by
    /// itself about nineteen seconds after the end.
    /// </summary>
    public TimeSpan ResultWait { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How many item boxes the shot of each turn may drop, in every game mode. The shooter's
    /// client places them along its missiles' paths; 0 lets no box drop. Between teams, the
    /// start of a turn allows fewer once the field is nearly full: see <see cref="MostItemBoxes"/>.
    /// </summary>
    public byte ItemBoxesPerTurn { get; set; } = 1;

    /// <summary>
    /// How good the items the clients draw are, from 0 to 100: higher makes the rarer
    /// items likelier. The reference sessions carry 20. The server's own draws, between
    /// teams, take every item as likely as another, whatever the quality.
    /// </summary>
    public uint ItemDropQuality { get; set; } = 20;

    /// <summary>
    /// How many item boxes the server places at the start of each turn of a match between
    /// teams, in a shot of its own, as many as the field has room for; 0 places none.
    /// </summary>
    public byte PlacedItemBoxesPerTurn { get; set; } = 3;

    /// <summary>
    /// How many item boxes may be on the field of a match between teams at once, the
    /// shooters' and the server's: once it is full, the server places none and the start of
    /// a turn lets the shot drop none, until boxes are caught.
    /// </summary>
    public byte MostItemBoxes { get; set; } = 28;

    /// <summary>The least height above the character nearest to it the server places an item box at.</summary>
    public int ItemBoxLowest { get; set; } = 100;

    /// <summary>The greatest height above the character nearest to it the server places an item box at.</summary>
    public int ItemBoxHighest { get; set; } = 300;

    /// <summary>
    /// The JSON file of the client's item drop table, which every box and bonus of a match
    /// between teams gives its item from. It is the client's data, kept by whoever runs the
    /// server and never in the repository; a relative path is looked for from the server's
    /// folder up. The server refuses to start without it. See <see cref="ItemDrops"/>.
    /// </summary>
    public string? ItemDropsFile { get; set; }

    /// <summary>What is wrong with the settings, for the message the server refuses to start with; null when nothing is.</summary>
    public string? Problem()
    {
        if (new[] { LevelReadyWait, LoadWait, LongestLoad, TurnWait, ShotWait, LongestShot, ResultWait }.Any(wait => wait <= TimeSpan.Zero))
        {
            return $"Every wait in {Section} must be positive";
        }

        if (TurnWait <= MatchRules.ClientTurnLength)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Section}:{nameof(TurnWait)} must be longer than the client's own turn, {MatchRules.ClientTurnLength.TotalSeconds} seconds, or it cuts turns the client still counts");
        }

        if (LongestLoad <= MatchRules.LongestClientTour)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Section}:{nameof(LongestLoad)} must be longer than the client's tour of a full room's characters, {MatchRules.LongestClientTour.TotalSeconds} seconds, or every full match starts before any client has loaded it");
        }

        if (ItemDropQuality > MaxDropQuality)
        {
            return $"{Section}:{nameof(ItemDropQuality)} must be from 0 to {MaxDropQuality}";
        }

        return ItemBoxLowest >= 0 && ItemBoxLowest <= ItemBoxHighest && ItemBoxHighest <= MaxItemBoxHeight
            ? null
            : $"{Section}:{nameof(ItemBoxLowest)} and {Section}:{nameof(ItemBoxHighest)} must be heights from 0 to {MaxItemBoxHeight}, the lowest first";
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
