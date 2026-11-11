using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.GamePlay;

namespace OpenTaan.GameServer.Matches;

/// <summary>Where a match stands while it is played.</summary>
internal enum MatchPhase
{
    /// <summary>
    /// Every client loads the match. A mission's first turn waits for its first level to be
    /// asked for and played open; the first turn of a match between teams, for every client
    /// to report the match loaded.
    /// </summary>
    Loading,

    /// <summary>A mission's level has begun, and its first turn waits for every client to play the level's opening.</summary>
    Opening,

    /// <summary>A character has the turn: it may walk, then shoot or pass.</summary>
    Turn,

    /// <summary>The turn's character has shot, and the turn waits for every client to play the shot back.</summary>
    Shot,
}

/// <summary>A room's match, a mission or a match between teams, from its start until the room waits for players again. Changed only under the room directory's lock.</summary>
/// <remarks>
/// <para>
/// The match keeps every character that started it, by slot, and which of them are out of
/// it: given up, gone with their player, or dead. A player out of the match still sees it
/// to its end, and its client still plays every level's opening and every shot, so it is
/// still waited for. Each character plays for the team of its slot; see <see cref="Winner"/>
/// for when that ends the match.
/// </para>
/// <para>
/// One timer at a time stands for the wait in progress: for the clients to load a match
/// between teams, or to play a mission level's opening, for the turn's character to shoot
/// or pass, for the clients to play a shot, then for the players to pick their result
/// cards. Every change of phase, and every new turn, moves <see cref="Step"/> on, and a
/// timer that fires for a step that is over finds nothing left to do.
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
    private readonly Dictionary<byte, (int X, int Y)> _ghostTargets = [];
    private readonly TurnOrder _turns = new();
    private ITimer? _timer;

    private Match(ushort mapId, MissionLevels? levels, uint seed, IReadOnlyDictionary<byte, ClientSession> players, DateTimeOffset started)
    {
        MapId = mapId;
        Levels = levels;
        Seed = seed;
        Started = started;
        _players = new Dictionary<byte, ClientSession>(players);
    }

    /// <summary>A match of mission <paramref name="missionId"/>, started by the characters in <paramref name="players"/>.</summary>
    /// <param name="missionId">The mission played.</param>
    /// <param name="levels">How many levels the mission has.</param>
    /// <param name="seed">The value every client lays the mission out from.</param>
    /// <param name="players">The connection playing the character in each slot, by slot.</param>
    /// <param name="started">When the match started.</param>
    public static Match OfMission(ushort missionId, byte levels, uint seed, IReadOnlyDictionary<byte, ClientSession> players, DateTimeOffset started) =>
        new(missionId, new MissionLevels(levels), seed, players, started);

    /// <summary>A match between teams on map <paramref name="mapId"/>, started by the characters in <paramref name="players"/>.</summary>
    /// <param name="mapId">The map played on.</param>
    /// <param name="players">The connection playing the character in each slot, by slot, each on its slot's team.</param>
    /// <param name="started">When the match started.</param>
    public static Match BetweenTeams(ushort mapId, IReadOnlyDictionary<byte, ClientSession> players, DateTimeOffset started) =>
        new(mapId, levels: null, MatchRules.NoMissionSeed, players, started);

    /// <summary>The map the match is played on, or a mission's own number in a mission.</summary>
    public ushort MapId { get; }

    /// <summary>Which of a mission's levels runs; null in a match between teams, which has none.</summary>
    public MissionLevels? Levels { get; }

    /// <summary>Whether the match is a mission's.</summary>
    public bool IsMission => Levels is not null;

    /// <summary>The value every client lays a mission out from; <see cref="MatchRules.NoMissionSeed"/> between teams.</summary>
    public uint Seed { get; }

    /// <summary>When the match started.</summary>
    public DateTimeOffset Started { get; }

    /// <summary>The result screen's cards, once the match is over.</summary>
    public ResultCards Cards { get; } = new();

    /// <summary>Where the match stands, until it is over.</summary>
    public MatchPhase Phase { get; private set; } = MatchPhase.Loading;

    /// <summary>Moves on with every change of phase and every new turn, so a timer can tell whether the wait it stands for is still on.</summary>
    public int Step { get; private set; }

    /// <summary>Whether the match's end has been sent: nothing about its turns follows.</summary>
    public bool IsOver { get; private set; }

    /// <summary>
    /// Whether a first turn waits for every client to report: in a mission, the first turn
    /// of a level whose opening the clients play; between teams, the match's first turn,
    /// while the clients load it.
    /// </summary>
    public bool AwaitsTurn => !IsOver && Phase == (IsMission ? MatchPhase.Opening : MatchPhase.Loading);

    /// <summary>Whether any client has reported what the first turn waits for.</summary>
    public bool AnyReady => _ready.Count > 0;

    /// <summary>The slot whose turn it is, or was last, once a turn has started.</summary>
    public byte? TurnSlot { get; private set; }

    /// <summary>The level a request made during the shot begins once every client has played the shot, if one does.</summary>
    public byte? HeldLevel { get; private set; }

    /// <summary>Whether a request made during the shot cleared the mission, which ends the match once every client has played the shot.</summary>
    public bool HeldClear { get; private set; }

    /// <summary>When the shot being played arrived.</summary>
    public DateTimeOffset ShotArrived { get; private set; }

    /// <summary>
    /// The turn count the start of each turn carries, which the client shows in a mission:
    /// how many shots the level's players have fired. A pass adds none, every level starts
    /// from 0, and a match between teams stays at 0.
    /// </summary>
    public ushort TurnCount { get; private set; }

    /// <summary>Whether any client has reported the shot being played finished.</summary>
    public bool AnyFinished => _finished.Count > 0;

    /// <summary>Starts waiting for every client to report the level that has just begun.</summary>
    public void LevelBegan()
    {
        Phase = MatchPhase.Opening;
        Step++;
        TurnCount = 0;
        _ready.Clear();
    }

    /// <summary>Records that <paramref name="session"/>'s client reports a mission's <paramref name="level"/> ready.</summary>
    /// <param name="session">The connection reporting.</param>
    /// <param name="level">The level it reports.</param>
    /// <param name="reason">Why the report changes nothing, for the log, when it does not.</param>
    /// <returns>Whether the report counts.</returns>
    public bool Ready(ClientSession session, byte level, out string? reason)
    {
        reason = Levels is not { } levels ? "the match is no mission"
            : level != levels.Current ? $"it reports level {level} ready while level {levels.Current} runs"
            : !AwaitsTurn ? $"level {level} has had its first turn"
            : !_ready.Add(session) ? $"it reported level {level} ready already"
            : null;
        return reason is null;
    }

    /// <summary>
    /// Records that <paramref name="session"/>'s client has loaded a match between teams, and
    /// returns whether that is news: only a client's first report counts, made while the
    /// match's first turn waits for it.
    /// </summary>
    public bool Loaded(ClientSession session) => !IsMission && AwaitsTurn && _ready.Add(session);

    /// <summary>Whether a first turn waits and every one of <paramref name="members"/> has reported what it waits for.</summary>
    public bool EveryoneReady(IEnumerable<ClientSession> members) => AwaitsTurn && members.All(_ready.Contains);

    /// <summary>The members of <paramref name="members"/> that have not reported what the first turn waits for.</summary>
    public IReadOnlyList<ClientSession> NotReady(IEnumerable<ClientSession> members) => [.. members.Where(member => !_ready.Contains(member))];

    /// <summary>
    /// Starts the first turn of the match, or of a mission's level, and returns its slot: the
    /// slot the turn was handed to, if its character is still in the match, otherwise the
    /// lowest one in it.
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
        _turns.Played(slot);
    }

    /// <summary>Hands the next turn to the character in <paramref name="slot"/>, to start once the level about to begin has been played open.</summary>
    public void HandNextTurnTo(byte slot) => TurnSlot = slot;

    /// <summary>
    /// The slot of the character still in the match whose turn comes after the turn of the
    /// one in <paramref name="slot"/>, as <see cref="TurnOrder"/> has it: the teams take turns,
    /// each through its characters in slot order, and <paramref name="slot"/> plays again only
    /// when no other can. Null when every character is out.
    /// </summary>
    public byte? NextInPlay(byte slot) => _turns.After(slot, InPlayOrdered());

    /// <summary>Whether the character in <paramref name="slot"/> started the match and is still in it.</summary>
    public bool InPlay(byte slot) => _players.ContainsKey(slot) && !_out.Contains(slot);

    /// <summary>Whether <paramref name="session"/> plays the character in <paramref name="slot"/>.</summary>
    public bool IsPlayedBy(byte slot, ClientSession session) => _players.TryGetValue(slot, out var player) && player == session;

    /// <summary>Whether it is the turn of the character in <paramref name="slot"/>, which <paramref name="session"/> plays and which is still in the match.</summary>
    public bool HasTurn(byte slot, ClientSession session) =>
        Phase == MatchPhase.Turn && TurnSlot == slot && IsPlayedBy(slot, session) && InPlay(slot);

    /// <summary>Starts waiting for every client to play back the shot the turn's character has just fired, which adds one to a mission level's turn count.</summary>
    /// <param name="now">When the shot arrived.</param>
    public void BeginShot(DateTimeOffset now)
    {
        Phase = MatchPhase.Shot;
        Step++;
        if (IsMission)
        {
            TurnCount = (ushort)Math.Min(TurnCount + 1, ushort.MaxValue);
        }

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

    /// <summary>Holds the mission level that has just begun, which is announced once every client has played the shot.</summary>
    public void HoldLevel() => HeldLevel = Levels?.Current;

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

    /// <summary>Records where the ghost of the character in <paramref name="slot"/> is to go.</summary>
    public void AimGhost(byte slot, int x, int y) => _ghostTargets[slot] = (x, y);

    /// <summary>Records where the ghost of the character in <paramref name="slot"/> stopped, and forgets its target once it is there.</summary>
    public void GhostStopped(byte slot, int x, int y)
    {
        Moved(slot, x, y);
        if (_ghostTargets.TryGetValue(slot, out var target) && target == (x, y))
        {
            _ghostTargets.Remove(slot);
        }
    }

    /// <summary>
    /// The move of every ghost that has not reached its target yet, in slot order: from where
    /// its client last reported it, or <paramref name="start"/> before any report, to its target.
    /// A ghost that stops short is sent on its way again with every turn, until it arrives.
    /// </summary>
    public IReadOnlyList<GhostMove> GhostMoves((int X, int Y) start) =>
    [
        .. _ghostTargets
            .OrderBy(ghost => ghost.Key)
            .Select(ghost => (Slot: ghost.Key, From: PositionOf(ghost.Key, start), To: ghost.Value))
            .Where(ghost => ghost.From != ghost.To)
            .Select(ghost => new GhostMove(ghost.Slot, ghost.From.X, ghost.From.Y, ghost.To.X, ghost.To.Y)),
    ];

    /// <summary>
    /// The team that has won the match, or null while it goes on: in a mission, the players'
    /// team once the mission is cleared, even by a shot that also killed its last player;
    /// otherwise the other team once one team has nobody left in play. A team that never had
    /// a character in the match, as a mission's own side, is never out. When one shot takes
    /// the last of both teams out, the team whose character fired it wins.
    /// </summary>
    public byte? Winner()
    {
        if (HeldClear)
        {
            return MatchRules.PlayersTeam;
        }

        var firstOut = IsTeamOut(1);
        var secondOut = IsTeamOut(2);
        return firstOut && secondOut ? Seating.TeamOf(TurnSlot ?? 0)
            : firstOut ? (byte)2
            : secondOut ? (byte)1
            : null;
    }

    /// <summary>Takes every character <paramref name="session"/> plays out of the match, and returns whether any was still in it.</summary>
    public bool GiveUp(ClientSession session)
    {
        var slots = SlotsOf(session);
        var wasIn = slots.Any(slot => !_out.Contains(slot));
        _out.UnionWith(slots);
        return wasIn;
    }

    /// <summary>
    /// Takes every character <paramref name="session"/> plays out of the match, and stops
    /// waiting for its client. Their ghosts leave the field with them, so none is moved again.
    /// </summary>
    public void Leave(ClientSession session)
    {
        foreach (var slot in SlotsOf(session))
        {
            _out.Add(slot);
            _ghostTargets.Remove(slot);
        }

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

    // Whether the team had characters in the match and every one of them is out.
    private bool IsTeamOut(byte team)
    {
        var members = _players.Keys.Where(slot => Seating.TeamOf(slot) == team).ToArray();
        return members.Length > 0 && members.All(_out.Contains);
    }

    private IEnumerable<byte> SlotsOf(ClientSession session) =>
        _players.Where(player => player.Value == session).Select(player => player.Key).ToArray();
}
