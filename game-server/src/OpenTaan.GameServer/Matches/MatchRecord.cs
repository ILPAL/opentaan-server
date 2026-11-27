namespace OpenTaan.GameServer.Matches;

/// <summary>How a character's part in a match ended.</summary>
public enum MatchEnding
{
    /// <summary>Its player saw the match to its end, its character alive or a ghost.</summary>
    Played,

    /// <summary>Its player gave up while the character was still in play, and saw the match to its end.</summary>
    GaveUp,

    /// <summary>Its player left the room before the match ended.</summary>
    Left,
}

/// <summary>What a match paid one character that started it.</summary>
/// <param name="CharacterId">The character.</param>
/// <param name="Slot">The slot it played in, whose team it played for.</param>
/// <param name="Ending">How its part in the match ended.</param>
/// <param name="Won">
/// Whether the match counts as won for it: its team's result, as its client counts it, or
/// lost for a character whose player left, whose client counts nothing.
/// </param>
/// <param name="Experience">The experience it was paid.</param>
/// <param name="Gold">The gold it was paid.</param>
public sealed record MatchPayment(uint CharacterId, byte Slot, MatchEnding Ending, bool Won, uint Experience, uint Gold);

/// <summary>A match that has ended, and what it paid each character that started it, to be kept once.</summary>
/// <param name="MatchId">The match, which no other match shares.</param>
/// <param name="RoomNumber">The room it was played in.</param>
/// <param name="ChannelId">The channel of that room.</param>
/// <param name="MapId">The map it was played on, or a mission's own number in a mission.</param>
/// <param name="MissionDifficulty">The difficulty a mission was played at; null in a match between teams.</param>
/// <param name="WinningTeam">The team that won.</param>
/// <param name="Started">When it started.</param>
/// <param name="Ended">When it ended.</param>
/// <param name="Payments">What it paid each character that started it, one entry for each.</param>
public sealed record MatchRecord(
    Guid MatchId,
    ushort RoomNumber,
    ushort ChannelId,
    ushort MapId,
    byte? MissionDifficulty,
    byte WinningTeam,
    DateTimeOffset Started,
    DateTimeOffset Ended,
    IReadOnlyList<MatchPayment> Payments);
