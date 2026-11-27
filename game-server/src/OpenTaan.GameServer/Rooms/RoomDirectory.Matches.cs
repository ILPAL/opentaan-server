using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>Matches: starting one, its first turns, a mission's levels, giving up, the end, and the result screen's cards. Turns are in RoomDirectory.Turns.cs.</summary>
/// <remarks>
/// <para>
/// A match lives in its room, under the same lock as every room, so what a match does and
/// what its room does, a player leaving among them, happen in one order for everyone. Its
/// waits run on their own, as timers on the server's clock: for a level's reports, for a
/// turn's shot or pass, for a shot to be played back, and for the result screen's cards.
/// Each takes the lock when it ends and finds nothing to do if the match has moved on.
/// </para>
/// <para>
/// From its start until the room waits again, a room shows as playing, in the lobby and to
/// anyone trying to enter; its waiting-room actions are dropped.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory
{
    // Why a request about a match is dropped when there is none to act on.
    private const string NotInMatch = "it is in no match being played";
    private const string NotInMission = "its match is no mission";
    private const string NotOnResultScreen = "it is in no match whose result is shown";
    private readonly Dictionary<Match, CardRound> _cardRounds = [];

    private sealed class CardRound(MatchRecord record, Task<bool> matchPayment)
    {
        public MatchRecord Record { get; } = record;
        public Task<bool> MatchPayment { get; } = matchPayment;
        public HashSet<byte> Revealed { get; } = [];
    }

    /// <summary>
    /// Starts the match of the host's room, and tells everyone in it and the lobby: every
    /// character in the room starts as <see cref="MatchRules"/> says, in the mission the
    /// room plays, laid out from a seed drawn for this match, or on the map a room of teams
    /// plays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host's client asks only once every other player in the room is ready, and does
    /// not wait for an answer, so a start the room's rules do not allow is dropped and only
    /// logged. A mission the client might not have is never started: it would leave every
    /// client in the room without a map.
    /// </para>
    /// <para>
    /// A match between teams starts whatever the size of each team, one of them even empty:
    /// the host's client lets a game master start one so, and any host's start is taken
    /// alike. In a mode with captains, each team's is drawn as it starts, and a team with no
    /// character has none; see <see cref="ChooseCaptains"/>. A room on one of the client's
    /// random picks is played on a map drawn for the match from the pick's maps among
    /// <paramref name="maps"/>; see <see cref="RoomOptions.RandomMaps"/>. Its first turn
    /// waits for every client to load it; see <see cref="ReportLoaded"/>.
    /// </para>
    /// </remarks>
    /// <param name="session">The connection asking.</param>
    /// <param name="maps">The maps a host may pick in a room of teams in the room's channel.</param>
    /// <returns>Null once started; otherwise why the request was dropped, for the log.</returns>
    public string? StartMatch(ClientSession session, IReadOnlyCollection<ushort> maps)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(maps);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (!room.IsHost(session))
            {
                return "only the host starts the match";
            }

            if (!room.GuestsReady)
            {
                return "a guest is not ready";
            }

            return room.IsMission ? StartMissionLocked(room) : StartBetweenTeamsLocked(room, maps);
        }
    }

    /// <summary>
    /// Records that <paramref name="session"/>'s client has loaded its match, and where its
    /// characters stand, for the ghosts they may become; in a match between teams, starts the
    /// first turn once every client in the match has reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client reports once, as soon as its camera has toured every character, and a turn
    /// reaching it before that would set its tour going again over the turn. So the first
    /// turn of a match between teams waits for every client, but no longer than
    /// <see cref="MatchOptions.LoadWait"/> once the first has reported: every client tours the
    /// same characters at the same pace. And no longer than <see cref="MatchOptions.LongestLoad"/>
    /// from the start in all, in case none reports. A mission's first turn waits for its
    /// first level instead.
    /// </para>
    /// <para>
    /// A report counts for its client whichever slots it names, since only the client knows
    /// it has loaded; where characters stand is taken from it only when every slot it names
    /// is one of the client's own.
    /// </para>
    /// </remarks>
    /// <returns>Null once recorded; otherwise why the report's places were dropped, for the log.</returns>
    public string? ReportLoaded(ClientSession session, PcGamePlayLoadingDone report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            var stranger = report.Characters.FirstOrDefault(character => !match.IsPlayedBy(character.SlotIndex, session));
            if (stranger is null)
            {
                foreach (var character in report.Characters)
                {
                    match.Moved(character.SlotIndex, character.X, character.Y);
                }
            }

            var first = !match.AnyReady;
            if (match.Loaded(session))
            {
                if (match.EveryoneReady(MembersOf(room)))
                {
                    StartTurnLocked(room, match);
                }
                else if (first)
                {
                    var now = time.GetUtcNow();
                    var due = Min(match.Started + matchOptions.Value.LongestLoad, now + matchOptions.Value.LoadWait) - now;
                    ArmLoadWaitLocked(room, match, due > TimeSpan.Zero ? due : TimeSpan.Zero);
                }
            }

            return stranger is null ? null : $"slot {stranger.SlotIndex} is not one of its characters";
        }
    }

    /// <summary>
    /// Begins the next level of the mission <paramref name="session"/> plays, when that is
    /// the level asked for, and tells everyone in the match; or ends the match as cleared,
    /// when the last level ran and the client has no next one. See <see cref="MissionLevels"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client asks for the first level once it has loaded the match, and for every later
    /// one, or for the clear, as it finishes playing back the shot that killed the level's
    /// last enemy. So a later level and the clear are taken only while a shot is played,
    /// and held until every client has played it: announced at once, the level's opening
    /// would start on a client still in the shot, and the end would cut its shot short.
    /// </para>
    /// <para>
    /// A level that begins waits for its first turn until every client in the match reports
    /// it ready, or for <see cref="MatchOptions.LevelReadyWait"/> at most. Whether its enemies
    /// take turns is the request's say; see <see cref="FinishEnemyTurn"/>.
    /// </para>
    /// </remarks>
    /// <param name="session">The connection asking.</param>
    /// <param name="level">The level asked for, or <see cref="PqGameSessionStartMissionLevel.NoNextLevel"/> for the clear.</param>
    /// <param name="wantsEnemyTurns">Whether the level's enemies take a turn after every shot, as the request says.</param>
    /// <returns>Null once done; otherwise why the request changed nothing, for the log.</returns>
    public string? AskForLevel(ClientSession session, byte level, bool wantsEnemyTurns)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (match.Levels is not { } levels)
            {
                return NotInMission;
            }

            var advances = level == levels.Current + 1 || level == PqGameSessionStartMissionLevel.NoNextLevel;
            if (advances && match.Phase is MatchPhase.Opening or MatchPhase.Turn or MatchPhase.EnemyTurn)
            {
                return $"level {level} was asked for while no shot is played";
            }

            switch (levels.Ask(level, wantsEnemyTurns, out var reason))
            {
                case LevelAnswer.Announce when match.Phase == MatchPhase.Shot:
                    match.HoldLevel();
                    return null;

                case LevelAnswer.Announce:
                    BeginLevelLocked(room, match, levels);
                    return null;

                case LevelAnswer.Clear when match.Phase == MatchPhase.Shot:
                    match.HoldClear();
                    return null;

                case LevelAnswer.Clear:
                    EndLocked(room, match, MatchRules.PlayersTeam);
                    return null;

                default:
                    return reason;
            }
        }
    }

    /// <summary>
    /// Records that <paramref name="session"/>'s client has played the opening of
    /// <paramref name="level"/>, and starts the level's first turn once every client in the
    /// match has.
    /// </summary>
    /// <remarks>
    /// The first turn goes to everyone in the match, the players out of it included, since
    /// their clients still show it. A turn reaching a client still in the level's opening
    /// would cut the opening short and leave that client without the level's enemies.
    /// </remarks>
    /// <returns>Null once recorded; otherwise why the report changed nothing, for the log.</returns>
    public string? ReportLevelReady(ClientSession session, byte level)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (!match.Ready(session, level, out var reason))
            {
                return reason;
            }

            if (match.EveryoneReady(MembersOf(room)))
            {
                StartTurnLocked(room, match);
            }

            return null;
        }
    }

    /// <summary>
    /// Takes every character <paramref name="session"/> plays out of its match: the turn
    /// passes on if it was one of theirs, and the match ends, lost, once every character
    /// that started it is out.
    /// </summary>
    /// <remarks>
    /// The others are told nothing: the client has no handler for a give-up sent to it. The
    /// player stays in the room, sees the match to its end, and picks its result card. See
    /// <see cref="ContinueAfterOutLocked"/> for what follows during a shot.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request changed nothing, for the log.</returns>
    public string? GiveUp(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (!match.GiveUp(session))
            {
                return "its characters are out of the match already";
            }

            LogGaveUp(logger, room.Number, session.Id);
            ContinueAfterOutLocked(room, match);
            return null;
        }
    }

    /// <summary>
    /// Turns over a result card for one of <paramref name="session"/>'s characters, and tells
    /// everyone still in the room. Once every character in the room has its card, the room
    /// waits for players again.
    /// </summary>
    /// <param name="session">The connection picking.</param>
    /// <param name="slot">
    /// The slot the request names. When it is not one of the connection's, the card is for
    /// the connection's first character.
    /// </param>
    /// <param name="card">The card picked, or null for the client's automatic pick, which takes one of those left.</param>
    /// <returns>Null once picked; otherwise why the pick was refused or dropped, for the log.</returns>
    public async ValueTask<string?> PickCardAsync(ClientSession session, byte slot, byte? card)
    {
        ArgumentNullException.ThrowIfNull(session);

        Room room;
        Match match;
        CardRound round;
        byte own;
        byte picked;
        MatchCardOffer offer;
        lock (_lock)
        {
            if (!_roomOf.TryGetValue(session, out room!) || room.Match is not { IsOver: true } current
                || !_cardRounds.TryGetValue(current, out round!))
            {
                return NotOnResultScreen;
            }

            match = current;
            var slots = room.SlotsOf(session);
            own = slots.Contains(slot) ? slot : slots[0];
            var outcome = match.Cards.Pick(own, card, random);
            picked = outcome.Card;
            switch (outcome.Outcome)
            {
                case CardOutcome.Picked:
                    var character = room.CharacterAt(own)
                        ?? throw new InvalidOperationException($"A result card was picked for empty slot {own}.");
                    offer = round.Record.Cards.SingleOrDefault(candidate => candidate.CharacterId == character.CharacterId)
                        ?? throw new InvalidOperationException($"Character {character.CharacterId} has no result card offer.");
                    break;

                case CardOutcome.Taken:
                    // Only a pick the player made itself is refused: the client waits on it.
                    session.Send(new PrResultAlreadyOpend(picked, own));
                    return $"card {picked} was taken first";

                case CardOutcome.AlreadyPicked:
                    return $"the character in slot {own} has card {picked} already";

                default:
                    return $"card {picked} is not one a player may pick";
            }
        }

        // The room lock is free while the card waits for the base match and its own
        // transaction. Its reservation prevents a second pick of either the slot or card.
        var paid = await payouts.PayCard(match.Id, offer, picked, round.MatchPayment,
            progress => CardPaid(room, match, round, session, own, picked, offer, progress)).ConfigureAwait(false);
        if (paid)
        {
            return null;
        }

        lock (_lock)
        {
            if (room.Match == match)
            {
                match.Cards.Release(own, picked);
            }
        }

        return $"card {picked} for character {offer.CharacterId} could not be paid";
    }

    private void CardPaid(
        Room room,
        Match match,
        CardRound round,
        ClientSession player,
        byte slot,
        byte card,
        MatchCardOffer offer,
        CharacterProgress progress)
    {
        lock (_lock)
        {
            if (room.Match == match && room.SlotOf(offer.CharacterId) == slot)
            {
                SendToRoomLocked(room, new PrResultSelectCard(card, slot));
                round.Revealed.Add(slot);
                if (room.TakenSlots().All(round.Revealed.Contains))
                {
                    BackToWaitingLocked(room);
                }
            }

            var accountSlot = player.SelectedCharacterIds.ToList().IndexOf(offer.CharacterId);
            if (accountSlot < 0)
            {
                return;
            }

            if (_roomOf.TryGetValue(player, out var currentRoom) && currentRoom.SlotOf(offer.CharacterId) is { } currentSlot)
            {
                currentRoom.Refresh(currentRoom.CharacterAt(currentSlot)!.WithProgress(progress));
            }

            if (offer.Kind == CardRewardKind.Experience)
            {
                var bar = LevelTable.BarAt(progress.Level)!.Value;
                player.Send(new PcUpdateExp((byte)accountSlot, progress.Level, progress.Experience, bar.Floor, bar.Ceiling));
            }
            else
            {
                player.Send(PcUpdateMoney.GoldOf((byte)accountSlot, progress.Gold));
            }
        }
    }

    // What a player leaving a room playing a match changes in it: everyone still in the
    // match is told its characters left. Called before they are taken out of the room.
    private void LeaveMatchLocked(Room room, ClientSession session)
    {
        if (room.Match is not { IsOver: false } match)
        {
            return;
        }

        match.Leave(session);
        foreach (var slot in room.SlotsOf(session))
        {
            SendToRoomLocked(room, new PcGamePlayLeaveUser(slot), except: session);
        }
    }

    // What the room's match does once a player has left the room, which still holds someone:
    // it goes on without the player, see ContinueAfterOutLocked, and once it is over the
    // room waits again if everyone left has a card.
    private void AfterLeavingMatchLocked(Room room)
    {
        switch (room.Match)
        {
            case { IsOver: false } match:
                ContinueAfterOutLocked(room, match);
                break;

            case { IsOver: true } match when room.TakenSlots().All(match.Cards.HasPicked):
                BackToWaitingLocked(room);
                break;
        }
    }

    // The room's members' connections.
    private static IEnumerable<ClientSession> MembersOf(Room room) => room.Members().Select(member => member.Session);

    // The room the connection is in, while it plays a match that is not over.
    private Room? PlayingRoomLocked(ClientSession session) =>
        _roomOf.TryGetValue(session, out var room) && room.Match is { IsOver: false } ? room : null;

    // Starts a mission's match: everyone in the room is told how it starts, and the lobby
    // that the room is playing.
    private string? StartMissionLocked(Room room)
    {
        if (!options.Value.Missions.TryGetValue(room.MapId, out var levels))
        {
            return $"mission {room.MapId} is not one the client lists";
        }

        BeginMatchLocked(room, Match.OfMission(room.MapId, levels, NextSeed(), room.PlayersBySlot(), room.CharacterIds(), time.GetUtcNow()));
        LogMissionStarted(logger, room.Number, room.MapId, levels);
        return null;
    }

    // Starts a match between the room's teams, whatever their sizes, on its map or, for a
    // random pick, on one drawn evenly from the pick's maps, with a captain drawn for each
    // team in a mode with captains. Its first turn waits for every client to load it.
    private string? StartBetweenTeamsLocked(Room room, IReadOnlyCollection<ushort> maps)
    {
        var map = room.MapId;
        if (MatchRules.RandomMapPicks.Contains(map))
        {
            var pool = options.Value.RandomPool(map, maps);
            if (pool.Count == 0)
            {
                return $"random pick {map} has no map to draw from in the room's channel";
            }

            map = pool[random.Next(pool.Count)];
        }

        var captains = MatchRules.HasCaptains(room.GameMode) ? DrawCaptainsLocked(room) : PcGameSessionStartGame.NoCaptains;
        var match = Match.BetweenTeams(map, room.PlayersBySlot(), room.CharacterIds(), captains, time.GetUtcNow());
        BeginMatchLocked(room, match);
        var (first, second) = room.TeamSizes();
        LogTeamMatchStarted(logger, room.Number, first, second, map, room.MapId);
        return null;
    }

    // Plays match in the room, and tells everyone in it how the match starts and the lobby
    // that the room is playing. A match between teams waits for its clients to load it from
    // before anyone hears of it.
    private void BeginMatchLocked(Room room, Match match)
    {
        room.BeginMatch(match);
        if (!match.IsMission)
        {
            ArmLoadWaitLocked(room, match, matchOptions.Value.LongestLoad);
        }

        SendToRoomLocked(room, new PcGameSessionStartGame(match.MapId, room.PlayData(), Wind: 0, Field24: 0, match.CaptainSlots, match.Seed));
        lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
    }

    // Begins the level the mission has just moved to, and tells everyone in the match. The
    // wait for its reports runs from before anyone hears of the level.
    private void BeginLevelLocked(Room room, Match match, MissionLevels levels)
    {
        var level = levels.Current;
        match.LevelBegan();
        match.Arm(time.CreateTimer(_ => LevelWaitElapsed(room, match, level), null, matchOptions.Value.LevelReadyWait, Timeout.InfiniteTimeSpan));
        SendToRoomLocked(room, new PcGameSessionStartMissionLevel(level));
    }

    // Starts the first turn of the match, or of the mission level running; see
    // Match.StartTurn for whose it is.
    private void StartTurnLocked(Room room, Match match)
    {
        var slot = match.StartTurn();
        ArmTurnWaitLocked(room, match);
        SendTurnStartLocked(room, match, slot);
        if (match.Levels is { } levels)
        {
            LogLevelTurnStarted(logger, room.Number, levels.Current, slot);
        }
        else
        {
            LogMatchTurnStarted(logger, room.Number, slot);
        }
    }

    // Ends the match with winningTeam the winner: everyone still in the room is sent the
    // rewards, then the result, which builds the result screen from them, and the room
    // waits for the cards. The result shows what the match pays each character, which is
    // kept after, away from this lock; see Paid.
    private void EndLocked(Room room, Match match, byte winningTeam)
    {
        // The wait for the cards runs from before anyone hears of the end.
        match.End();
        match.Arm(time.CreateTimer(_ => ResultWaitElapsed(room, match), null, matchOptions.Value.ResultWait, Timeout.InfiniteTimeSpan));
        room.ClearReady();
        var payments = PaymentsOf(room, match, winningTeam);
        var characters = room.CharacterIds();
        var cards = CardRewards.Roll(characters, payments, rewards.Value, random);
        SendToRoomLocked(room, CardRewards.PacketOf(characters, cards));
        SendToRoomLocked(room, new PcGameSessionEndGame(ResultOf(room, winningTeam, payments), EndGameMode: 0));

        var record = new MatchRecord(
            match.Id,
            room.Number,
            room.ChannelId,
            match.MapId,
            match.IsMission ? room.Difficulty : null,
            winningTeam,
            match.Started,
            time.GetUtcNow(),
            payments)
        {
            Cards = cards,
        };
        var players = match.Characters().DistinctBy(started => started.CharacterId).ToDictionary(started => started.CharacterId, started => started.Player);
        var payment = payouts.Pay(record, kept => Paid(record, players, kept));
        _cardRounds.Add(match, new CardRound(record, payment));
        LogMatchEnded(
            logger,
            room.Number,
            !match.IsMission ? $"team {winningTeam} won"
            : winningTeam == MatchRules.PlayersTeam ? "the mission was cleared"
            : "the mission was lost");
    }

    // What the match pays each character that started it. A character whose player saw the
    // match to its end is paid, alive or a ghost, and counts its team's result, as its client
    // does; one whose player gave up is paid nothing and counts its team's result too; one
    // whose player left is paid nothing and counts the match lost, as its client counts
    // nothing. A mission pays by its own number, its difficulty and whether it was cleared,
    // the same to every player; a match between teams, by the character's team's result.
    private MatchPayment[] PaymentsOf(Room room, Match match, byte winningTeam) =>
    [
        .. match.Characters().Select(started =>
        {
            var ending = match.EndingOf(started.Slot);
            var won = ending != MatchEnding.Left && Seating.TeamOf(started.Slot) == winningTeam;
            var earned = ending != MatchEnding.Played ? default
                : match.IsMission ? rewards.Value.Mission(match.MapId, room.Difficulty, cleared: winningTeam == MatchRules.PlayersTeam)
                : rewards.Value.BetweenTeams(won);
            return new MatchPayment(started.CharacterId, started.Slot, ending, won, earned.Experience, earned.Gold);
        }),
    ];

    // The result, with a row for each slot: what a character still in the room was paid,
    // from where it stood, or nothing for one whose player gave up; and nothing to show
    // for a slot with no character.
    private static GameResult ResultOf(Room room, byte winningTeam, IReadOnlyList<MatchPayment> payments) => new(
        winningTeam,
        [
            .. Enumerable.Range(0, GameSlotInfo.SlotCount).Select(slot =>
            {
                var place = (byte)slot;
                if (room.CharacterAt(slot) is not { } character)
                {
                    return GameResultSlot.Empty(place);
                }

                return payments.FirstOrDefault(payment => payment.Slot == place && payment.CharacterId == character.CharacterId) is
                    { Ending: MatchEnding.Played } paid
                    ? MatchRules.PaidRow(place, CharacterProgress.Of(character.Info), new Earning(paid.Experience, paid.Gold))
                    : GameResultSlot.NothingEarned(place, character.Info.Level);
            }),
        ],
        WinnerPointDelta: 0,
        LoserPointDelta: 0);

    // What a match paid is kept: each of its characters a connection still plays is put
    // back in the room it is in as it is kept now, so that a player entering later is shown
    // its level, and a character that played the match to its end is told its experience
    // and gold, in place of its client's own. The client keeps those only from these
    // packets; it counts the match won or lost itself.
    private void Paid(MatchRecord record, IReadOnlyDictionary<uint, ClientSession> players, IReadOnlyDictionary<uint, CharacterProgress> kept)
    {
        lock (_lock)
        {
            foreach (var payment in record.Payments)
            {
                var player = players[payment.CharacterId];
                var accountSlot = player.SelectedCharacterIds.ToList().IndexOf(payment.CharacterId);
                if (!kept.TryGetValue(payment.CharacterId, out var progress) || accountSlot < 0)
                {
                    // Deleted, or no longer played by that connection: nothing of it is held.
                    continue;
                }

                if (_roomOf.TryGetValue(player, out var room) && room.SlotOf(payment.CharacterId) is { } slot)
                {
                    room.Refresh(room.CharacterAt(slot)!.WithProgress(progress));
                }

                if (payment.Ending == MatchEnding.Played)
                {
                    var bar = LevelTable.BarAt(progress.Level)!.Value;
                    player.Send(new PcUpdateExp((byte)accountSlot, progress.Level, progress.Experience, bar.Floor, bar.Ceiling));
                    player.Send(PcUpdateMoney.GoldOf((byte)accountSlot, progress.Gold));
                }
            }
        }
    }

    private void BackToWaitingLocked(Room room)
    {
        if (room.Match is { } match)
        {
            _cardRounds.Remove(match);
        }

        room.EndMatch();
        lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
        LogWaitingAgain(logger, room.Number);
    }

    private void LevelWaitElapsed(Room room, Match match, byte level)
    {
        lock (_lock)
        {
            if (room.Match != match || !match.AwaitsTurn || match.Levels?.Current != level)
            {
                return;
            }

            var late = match.NotReady(MembersOf(room));
            LogLevelWaitElapsed(logger, room.Number, level, string.Join(", ", late.Select(member => member.Id)));
            StartTurnLocked(room, match);
        }
    }

    private void ArmLoadWaitLocked(Room room, Match match, TimeSpan due)
    {
        var step = match.Step;
        match.Arm(time.CreateTimer(_ => LoadWaitElapsed(room, match, step), null, due, Timeout.InfiniteTimeSpan));
    }

    // Not every client reported the match loaded in time: its first turn starts without
    // waiting for the rest.
    private void LoadWaitElapsed(Room room, Match match, int step)
    {
        lock (_lock)
        {
            if (room.Match != match || match.Step != step || !match.AwaitsTurn)
            {
                return;
            }

            var late = match.NotReady(MembersOf(room));
            LogLoadWaitElapsed(logger, room.Number, string.Join(", ", late.Select(member => member.Id)));
            StartTurnLocked(room, match);
        }
    }

    private void ResultWaitElapsed(Room room, Match match)
    {
        lock (_lock)
        {
            if (room.Match == match)
            {
                BackToWaitingLocked(room);
            }
        }
    }

    // Any of the 2^32 values, evenly.
    private uint NextSeed() => (uint)random.NextInt64(0, 1L << 32);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber} started mission {MissionId}, of {Levels} levels")]
    private static partial void LogMissionStarted(ILogger logger, ushort roomNumber, ushort missionId, byte levels);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Room {RoomNumber} started a match of {FirstTeam} against {SecondTeam} on map {MapId}, for the room's map {RoomMapId}")]
    private static partial void LogTeamMatchStarted(ILogger logger, ushort roomNumber, int firstTeam, int secondTeam, ushort mapId, ushort roomMapId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: level {Level}'s first turn went to slot {Slot}")]
    private static partial void LogLevelTurnStarted(ILogger logger, ushort roomNumber, byte level, byte slot);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: the match's first turn went to slot {Slot}")]
    private static partial void LogMatchTurnStarted(ILogger logger, ushort roomNumber, byte slot);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Room {RoomNumber}: level {Level}'s first turn started without a report from connections {Connections}")]
    private static partial void LogLevelWaitElapsed(ILogger logger, ushort roomNumber, byte level, string connections);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Room {RoomNumber}: the match's first turn started without connections {Connections} reporting it loaded")]
    private static partial void LogLoadWaitElapsed(ILogger logger, ushort roomNumber, string connections);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: connection {ConnectionId} gave up its match")]
    private static partial void LogGaveUp(ILogger logger, ushort roomNumber, long connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}'s match ended: {Outcome}")]
    private static partial void LogMatchEnded(ILogger logger, ushort roomNumber, string outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber} waits for players again")]
    private static partial void LogWaitingAgain(ILogger logger, ushort roomNumber);
}
