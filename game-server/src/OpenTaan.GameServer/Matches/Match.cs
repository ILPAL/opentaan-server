using OpenTaan.GameServer.Sessions;

namespace OpenTaan.GameServer.Matches;

/// <summary>A mission room's match, from its start until the room waits for players again. Changed only under the room directory's lock.</summary>
/// <remarks>
/// <para>
/// The match keeps every character that started it, by slot, and which of them are out of
/// it: given up, or gone with their player. A player out of the match still sees it to its
/// end, and its client still plays each level's opening, so it is still waited for.
/// </para>
/// <para>
/// One timer at a time stands for the wait in progress: for the clients to report a level
/// ready, then for the players to pick their result cards. A timer that fires for a match
/// that has moved on finds nothing left to do.
/// </para>
/// </remarks>
internal sealed class Match : IDisposable
{
    private readonly Dictionary<byte, ClientSession> _players;
    private readonly HashSet<byte> _out = [];
    private readonly HashSet<ClientSession> _ready = [];
    private ITimer? _timer;

    /// <summary>A match of mission <paramref name="missionId"/>, started by the characters in <paramref name="players"/>.</summary>
    /// <param name="missionId">The mission played.</param>
    /// <param name="levels">How many levels the mission has.</param>
    /// <param name="seed">The value every client lays the mission out from.</param>
    /// <param name="players">The connection playing the character in each slot, by slot.</param>
    public Match(ushort missionId, byte levels, uint seed, IReadOnlyDictionary<byte, ClientSession> players)
    {
        MissionId = missionId;
        Seed = seed;
        Levels = new MissionLevels(levels);
        _players = new Dictionary<byte, ClientSession>(players);
    }

    /// <summary>The mission played.</summary>
    public ushort MissionId { get; }

    /// <summary>The value every client lays the mission out from.</summary>
    public uint Seed { get; }

    /// <summary>Which of the mission's levels runs.</summary>
    public MissionLevels Levels { get; }

    /// <summary>The result screen's cards, once the match is over.</summary>
    public ResultCards Cards { get; } = new();

    /// <summary>Whether the match's end has been sent: nothing about its turns follows.</summary>
    public bool IsOver { get; private set; }

    /// <summary>Whether every character that started the match is out of it.</summary>
    public bool IsLost => _players.Keys.All(_out.Contains);

    /// <summary>Whether the level running waits for its first turn.</summary>
    public bool AwaitsTurn => !IsOver && Levels.Current > 0 && !HasTurn;

    /// <summary>The slot whose turn it is, once a turn has started.</summary>
    public byte? TurnSlot { get; private set; }

    // Whether the level running has had its first turn.
    private bool HasTurn { get; set; }

    /// <summary>Starts waiting for every client to report the level that has just begun.</summary>
    public void LevelBegan()
    {
        _ready.Clear();
        HasTurn = false;
    }

    /// <summary>Records that <paramref name="session"/>'s client reports <paramref name="level"/> ready.</summary>
    /// <param name="session">The connection reporting.</param>
    /// <param name="level">The level it reports.</param>
    /// <param name="reason">Why the report changes nothing, for the log, when it does not.</param>
    /// <returns>Whether the report counts.</returns>
    public bool Ready(ClientSession session, byte level, out string? reason)
    {
        reason = level != Levels.Current ? $"it reports level {level} ready while level {Levels.Current} runs"
            : !AwaitsTurn ? $"level {level} has had its first turn"
            : !_ready.Add(session) ? $"it reported level {level} ready already"
            : null;
        return reason is null;
    }

    /// <summary>Whether the level running waits for its first turn and every one of <paramref name="members"/> has reported it ready.</summary>
    public bool EveryoneReady(IEnumerable<ClientSession> members) => AwaitsTurn && members.All(_ready.Contains);

    /// <summary>The members of <paramref name="members"/> that have not reported the level running ready.</summary>
    public IReadOnlyList<ClientSession> NotReady(IEnumerable<ClientSession> members) => [.. members.Where(member => !_ready.Contains(member))];

    /// <summary>Starts the level's first turn, and returns its slot: the turn's character's if it is still in the match, otherwise the lowest one in it.</summary>
    /// <exception cref="InvalidOperationException">Every character is out of the match.</exception>
    public byte StartTurn()
    {
        var inPlay = _players.Keys.Where(slot => !_out.Contains(slot)).Order().ToArray();
        if (inPlay.Length == 0)
        {
            throw new InvalidOperationException("A turn starts only while a character is in the match.");
        }

        HasTurn = true;
        TurnSlot = TurnSlot is { } current && inPlay.Contains(current) ? current : inPlay[0];
        return TurnSlot.Value;
    }

    /// <summary>Takes every character <paramref name="session"/> plays out of the match, and returns whether any was still in it.</summary>
    public bool GiveUp(ClientSession session)
    {
        var slots = SlotsOf(session);
        var wasIn = slots.Any(slot => !_out.Contains(slot));
        _out.UnionWith(slots);
        return wasIn;
    }

    /// <summary>Takes every character <paramref name="session"/> plays out of the match, and stops waiting for its client.</summary>
    public void Leave(ClientSession session)
    {
        _out.UnionWith(SlotsOf(session));
        _ready.Remove(session);
    }

    /// <summary>Marks the match over, and stops any wait in progress.</summary>
    public void End()
    {
        IsOver = true;
        Disarm();
    }

    /// <summary>Makes <paramref name="timer"/> the wait in progress, ending any other.</summary>
    public void Arm(ITimer timer)
    {
        Disarm();
        _timer = timer;
    }

    /// <summary>Ends the wait in progress, if any.</summary>
    public void Disarm()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Ends the wait in progress, if any: the match is of no further use.</summary>
    public void Dispose() => Disarm();

    private IEnumerable<byte> SlotsOf(ClientSession session) =>
        _players.Where(player => player.Value == session).Select(player => player.Key).ToArray();
}
