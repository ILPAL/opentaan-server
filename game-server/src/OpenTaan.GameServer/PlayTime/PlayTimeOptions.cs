namespace OpenTaan.GameServer.PlayTime;

/// <summary>Settings bound from the <c>PlayTime</c> configuration section.</summary>
/// <remarks>
/// Some regional builds of the client, the Vietnamese one among them, limit how long a
/// player may play, and must be told the limit before they show a lobby: told nothing,
/// they treat the limit as used up and quit. See <see cref="PlayTimeGrant"/>.
/// </remarks>
public sealed class PlayTimeOptions
{
    public const string Section = "PlayTime";

    /// <summary>
    /// How long a player may play, in seconds, or null for no limit. The client keeps the
    /// count and enforces it, at its next lobby; this server does not keep played time
    /// yet, so the count starts again at every channel entry.
    /// </summary>
    public uint? AllowedSeconds { get; set; }
}
