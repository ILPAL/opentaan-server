using OpenTaan.GameServer.Matches;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// What a match pays a character with the rewards the server ships with, written out from
/// the amounts agreed for them rather than worked out by the server's own code.
/// </summary>
internal static class Paying
{
    /// <summary>A match between teams, won: 40 experience and 120 gold, each times 1.5.</summary>
    public static readonly Earning TeamWin = new(60, 180);

    /// <summary>A match between teams, lost: 40 experience and 120 gold.</summary>
    public static readonly Earning TeamLoss = new(40, 120);

    /// <summary>Mission 0 at its easiest difficulty, cleared: 40 experience, and 1.5 gold for each.</summary>
    public static readonly Earning MissionCleared = new(40, 60);

    /// <summary>Mission 0 at its easiest difficulty, lost: a quarter of the clear's experience, and 1.5 gold for each.</summary>
    public static readonly Earning MissionLost = new(10, 15);
}
