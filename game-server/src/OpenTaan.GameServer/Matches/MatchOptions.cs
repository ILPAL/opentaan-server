namespace OpenTaan.GameServer.Matches;

/// <summary>Settings bound from the <c>Matches</c> configuration section.</summary>
/// <remarks>
/// Both are how long the server waits on players before it goes on without them, so that
/// one client that stops answering cannot hold up everyone else.
/// </remarks>
public sealed class MatchOptions
{
    public const string Section = "Matches";

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
}
