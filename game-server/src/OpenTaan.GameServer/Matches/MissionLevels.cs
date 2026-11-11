using OpenTaan.Protocol.Matches;

namespace OpenTaan.GameServer.Matches;

/// <summary>What a request for a mission level leads to.</summary>
public enum LevelAnswer
{
    /// <summary>The level is the next one: it begins, and everyone in the match is told.</summary>
    Announce,

    /// <summary>The last level ran and the client has no next one: the mission is cleared.</summary>
    Clear,

    /// <summary>Nothing: a copy of a request already answered, or a level the mission cannot reach now.</summary>
    Ignore,
}

/// <summary>Which of a mission's levels runs, and which a client may ask for next.</summary>
/// <remarks>
/// <para>
/// Every client asks for every level: for the first once it has loaded the match, for each
/// next one once its own count of the enemies killed reaches the level's target, and for
/// <see cref="PqGameSessionStartMissionLevel.NoNextLevel"/> once the mission has no next
/// one. So only the next level begins, once, and a mission is cleared only while its last
/// level runs; every other request, the copies from the other clients among them, changes
/// nothing. Announcing a level twice would restart its opening on every client.
/// </para>
/// <para>
/// The server cannot count the kills itself: they happen in each client's play of a shot.
/// What it can hold every client to is the order of the levels, and their number, which
/// the client's own copy of the mission sets.
/// </para>
/// </remarks>
/// <param name="count">How many levels the mission has.</param>
public sealed class MissionLevels(byte count)
{
    /// <summary>How many levels the mission has.</summary>
    public byte Count { get; } = count;

    /// <summary>The level running, from 1, or 0 before the first begins.</summary>
    public byte Current { get; private set; }

    /// <summary>What a request for <paramref name="level"/> leads to. A level that begins becomes <see cref="Current"/>.</summary>
    /// <param name="level">The level asked for.</param>
    /// <param name="reason">Why the request changes nothing, for the log, when it does not.</param>
    public LevelAnswer Ask(byte level, out string? reason)
    {
        if (level == PqGameSessionStartMissionLevel.NoNextLevel)
        {
            reason = Current == Count ? null : $"the mission was cleared at level {Current} of its {Count}";
            return reason is null ? LevelAnswer.Clear : LevelAnswer.Ignore;
        }

        if (level != Current + 1 || level > Count)
        {
            reason = level <= Current
                ? $"level {level} has begun already"
                : $"level {level} was asked for while level {Current} of {Count} runs";
            return LevelAnswer.Ignore;
        }

        Current = level;
        reason = null;
        return LevelAnswer.Announce;
    }
}
