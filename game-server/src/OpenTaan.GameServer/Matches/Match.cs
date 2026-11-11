using OpenTaan.GameServer.Sessions;

namespace OpenTaan.GameServer.Matches;

/// <summary>Where a match stands while it is played.</summary>
internal enum MatchPhase
{
    /// <summary>Every client loads the match; no level has begun.</summary>
    Loading,

    /// <summary>A level has begun, and its first turn waits for every client to play the level's opening.</summary>
    Opening,

    /// <summary>A character has the turn: it may walk, then shoot or pass.</summary>
    Turn,

    /// <summary>The turn's character has shot, and the turn waits for every client to play the shot back.</summary>
    Shot,
}

/// <summary>A mission room's match, from its start until the room waits for players again. Changed only under the room directory's lock.</summary>
/// <remarks>
/// <para>
/// The match keeps every character that started it, by slot, and which of them are out of
/// it: given up, gone with their player, or dead. A player out of the match still sees it
/// to its end, and its client still plays every level's opening and every shot, so it is
/// still waited for.
/// </para>
/// <para>
/// One timer at a time stands for the wait in progress: for the clients to play a level's
/// opening, for the turn's character to shoot or pass, for the clients to play a shot,
/// then for the players to pick their result cards. Every change of phase, and every new
/// turn, moves <see cref="Step"/> on, and a timer that fires for a step that is over finds
/// nothing left to do.
/// </para>
/// </remarks>
internal sealed class Match : IDisposable
{
    private readonly Dictionary<byte, ClientSession> _players;
    private readonly HashSet<byte> _out = [];
    private readonly HashSet<byte> _dead = [];
    private readonly HashSet<ClientSession> _ready = [];
    private readonly HashSet<ClientSession> _finished = [];
    private readonly Dictionary<byte, (int X, int Y)> _positions = [];
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

    /// <summary>Where the match stands, until it is over.</summary>
    public MatchPhase Phase { get; private set; } = MatchPhase.Loading;

    /// <summary>Moves on with every change of phase and every new turn, so a timer can tell whether the wait it stands for is still on.</summary>
    public int Step { get; private set; }

    /// <summary>Whether the match's end has been sent: nothing about its turns follows.</summary>
    public bool IsOver { get; private set; }

    /// <summary>Whether every character that started the match is out of it.</summary>
    public bool IsLost => _players.Keys.All(_out.Contains);

    /// <summary>Whether the level running waits for its first turn.</summary>
    public bool AwaitsTurn => !IsOver && Phase == MatchPhase.Opening;

    /// <summary>The slot whose turn it is, or was last, once a turn has started.</summary>
    public byte? TurnSlot { get; private set; }

    /// <summary>The level a request made during the shot begins once every client has played the shot, if one does.</summary>
    public byte? HeldLevel { get; private set; }

    /// <summary>Whether a request made during the shot cleared the mission, which ends the match once every client has played the shot.</summary>
    public bool HeldClear { get; private set; }

    /// <summary>When the shot being played arrived.</summary>
    public DateTimeOffset ShotArrived { get; private set; }

    /// <summary>Whether any client has reported the shot being played finished.</summary>
    public bool AnyFinished => _finished.Count > 0;

    /// <summary>Starts waiting for every client to report the level that has just begun.</summary>
    public void LevelBegan()
    {
        Phase = MatchPhase.Opening;
        Step++;
        _ready.Clear();
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

    /// <summary>
    /// Starts the level's first turn, and returns its slot: the slot the turn was handed to,
    /// if its character is still in the match, otherwise the lowest one in it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Every character is out of the match.</exception>
    public byte StartTurn()
    {
        var inPlay = InPlayOrdered();
        if (inPlay.Length == 0)
        {
            throw new InvalidOperationException("A turn starts only while a character is in the match.");
        }

        HandTurnTo(TurnSlot is { } current && inPlay.Contains(current) ? current : inPlay[0]);
        return TurnSlot!.Value;
    }

    /// <summary>Starts the turn of the character in <paramref name="slot"/>.</summary>
    public void HandTurnTo(byte slot)
    {
        TurnSlot = slot;
        Phase = MatchPhase.Turn;
        Step++;
    }

    /// <summary>Hands the next turn to the character in <paramref name="slot"/>, to start once the level about to begin has been played open.</summary>
    public void HandNextTurnTo(byte slot) => TurnSlot = slot;

    /// <summary>
    /// The slot of the first character in the match after <paramref name="slot"/>, in slot
    /// order and round again from the lowest, <paramref name="slot"/> itself last; null when
    /// every character is out.
    /// </summary>
    public byte? NextInPlay(byte slot)
    {
        var inPlay = InPlayOrdered();
        return inPlay.Length == 0 ? null : inPlay.FirstOrDefault(other => other > slot, inPlay[0]);
    }

    /// <summary>Whether the character in <paramref name="slot"/> started the match and is still in it.</summary>
    public bool InPlay(byte slot) => _players.ContainsKey(slot) && !_out.Contains(slot);

    /// <summary>Whether <paramref name="session"/> plays the character in <paramref name="slot"/>.</summary>
    public bool IsPlayedBy(byte slot, ClientSession session) => _players.TryGetValue(slot, out var player) && player == session;

    /// <summary>Whether it is the turn of the character in <paramref name="slot"/>, which <paramref name="session"/> plays and which is still in the match.</summary>
    public bool HasTurn(byte slot, ClientSession session) =>
        Phase == MatchPhase.Turn && TurnSlot == slot && IsPlayedBy(slot, session) && InPlay(slot);

    /// <summary>Starts waiting for every client to play back the shot the turn's character has just fired.</summary>
    /// <param name="now">When the shot arrived.</param>
    public void BeginShot(DateTimeOffset now)
    {
        Phase = MatchPhase.Shot;
        Step++;
        ShotArrived = now;
        HeldLevel = null;
        HeldClear = false;
        _finished.Clear();
    }

    /// <summary>
    /// Records that <paramref name="session"/>'s client has played the shot back, and returns
    /// whether that is news: each client reports a shot twice, and the second report, which
    /// may come once the shot is over, changes nothing.
    /// </summary>
    public bool Finished(ClientSession session) => _finished.Add(session);

    /// <summary>Whether <paramref name="session"/>'s client has reported the last shot played back.</summary>
    public bool HasFinished(ClientSession session) => _finished.Contains(session);

    /// <summary>Whether a shot is being played and every one of <paramref name="members"/> has reported it played back.</summary>
    public bool EveryoneFinished(IEnumerable<ClientSession> members) => !IsOver && Phase == MatchPhase.Shot && members.All(_finished.Contains);

    /// <summary>The members of <paramref name="members"/> that have not reported the shot being played back.</summary>
    public IReadOnlyList<ClientSession> NotFinished(IEnumerable<ClientSession> members) => [.. members.Where(member => !_finished.Contains(member))];

    /// <summary>Holds the level that has just begun, which is announced once every client has played the shot.</summary>
    public void HoldLevel() => HeldLevel = Levels.Current;

    /// <summary>Holds the mission's clear, which ends the match once every client has played the shot.</summary>
    public void HoldClear() => HeldClear = true;

    /// <summary>Takes the character in <paramref name="slot"/> out of the match as dead, and returns whether it was not dead already.</summary>
    public bool Died(byte slot)
    {
        _out.Add(slot);
        return _dead.Add(slot);
    }

    /// <summary>Whether the character in <paramref name="slot"/> has died in the match.</summary>
    public bool IsDead(byte slot) => _dead.Contains(slot);

    /// <summary>Records where the character in <paramref name="slot"/> stands now.</summary>
    public void Moved(byte slot, int x, int y) => _positions[slot] = (x, y);

    /// <summary>Where the character in <paramref name="slot"/> was last reported standing, or <paramref name="start"/> before any report.</summary>
    public (int X, int Y) PositionOf(byte slot, (int X, int Y) start) => _positions.GetValueOrDefault(slot, start);

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
        _finished.Remove(session);
    }

    /// <summary>Marks the match over, and stops any wait in progress.</summary>
    public void End()
    {
        IsOver = true;
        Step++;
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

    private byte[] InPlayOrdered() => [.. _players.Keys.Where(slot => !_out.Contains(slot)).Order()];

    private IEnumerable<byte> SlotsOf(ClientSession session) =>
        _players.Where(player => player.Value == session).Select(player => player.Key).ToArray();
}
