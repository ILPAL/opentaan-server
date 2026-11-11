using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Rooms;

/// <summary>Matches: starting one, a mission's levels and its first turns, giving up, the end, and the result screen's cards.</summary>
/// <remarks>
/// <para>
/// A match lives in its room, under the same lock as every room, so what a match does and
/// what its room does, a player leaving among them, happen in one order for everyone. Two
/// waits run on their own, as timers on the server's clock: for a level's reports, and for
/// the result screen's cards. Each takes the lock when it ends and finds nothing to do if
/// the match has moved on.
/// </para>
/// <para>
/// From its start until the room waits again, a room shows as playing, in the lobby and to
/// anyone trying to enter; its waiting-room actions are dropped. Matches between teams are
/// not played yet: their host's start is not answered.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory
{
    // Why a request about a match is dropped when there is none to act on.
    private const string NotInMatch = "it is in no match being played";
    private const string NotOnResultScreen = "it is in no match whose result is shown";

    /// <summary>
    /// Starts the match of the host's room, and tells everyone in it and the lobby: every
    /// character in the room starts as <see cref="MatchRules"/> says, in the mission the
    /// room plays, laid out from a seed drawn for this match.
    /// </summary>
    /// <remarks>
    /// The host's client asks only once every guest is ready, and does not wait for an
    /// answer, so a start the room's rules do not allow is dropped and only logged. A
    /// mission the client might not have is never started: it would leave every client in
    /// the room without a map.
    /// </remarks>
    /// <returns>Null once started; otherwise why the request was dropped, for the log.</returns>
    public string? StartMatch(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

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

            if (!room.IsMission)
            {
                return "matches between teams are not played yet";
            }

            if (!room.GuestsReady)
            {
                return "a guest is not ready";
            }

            if (!options.Value.Missions.TryGetValue(room.MapId, out var levels))
            {
                return $"mission {room.MapId} is not one the client lists";
            }

            var match = new Match(room.MapId, levels, NextSeed(), room.PlayersBySlot());
            room.BeginMatch(match);
            SendToRoomLocked(room, new PcGameSessionStartGame(match.MissionId, room.PlayData(), Wind: 0, Field24: 0, MatchRules.CaptainSlots, match.Seed));
            lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
            LogMatchStarted(logger, room.Number, match.MissionId, levels);
            return null;
        }
    }

    /// <summary>
    /// Begins the next level of the mission <paramref name="session"/> plays, when that is
    /// the level asked for, and tells everyone in the match; or ends the match as cleared,
    /// when the last level ran and the client has no next one. See <see cref="MissionLevels"/>.
    /// </summary>
    /// <remarks>
    /// A level that begins waits for its first turn until every client in the match reports
    /// it ready, or for <see cref="MatchOptions.LevelReadyWait"/> at most.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request changed nothing, for the log.</returns>
    public string? AskForLevel(ClientSession session, byte level)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            switch (match.Levels.Ask(level, out var reason))
            {
                case LevelAnswer.Announce:
                    // The wait runs from before anyone hears of the level.
                    match.LevelBegan();
                    match.Arm(time.CreateTimer(_ => LevelWaitElapsed(room, match, level), null, matchOptions.Value.LevelReadyWait, Timeout.InfiniteTimeSpan));
                    SendToRoomLocked(room, new PcGameSessionStartMissionLevel(level));
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
    /// Takes every character <paramref name="session"/> plays out of its match, and ends the
    /// match, lost, once every character that started it is out.
    /// </summary>
    /// <remarks>
    /// The others are told nothing: the client has no handler for a give-up sent to it. The
    /// player stays in the room, sees the match to its end, and picks its result card.
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
            if (match.IsLost)
            {
                EndLocked(room, match, MatchRules.MissionsTeam);
            }

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
    public string? PickCard(ClientSession session, byte slot, byte? card)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (!_roomOf.TryGetValue(session, out var room) || room.Match is not { IsOver: true } match)
            {
                return NotOnResultScreen;
            }

            var slots = room.SlotsOf(session);
            var own = slots.Contains(slot) ? slot : slots[0];
            var (outcome, picked) = match.Cards.Pick(own, card, random);
            switch (outcome)
            {
                case CardOutcome.Picked:
                    SendToRoomLocked(room, new PrResultSelectCard(picked, own));
                    if (room.TakenSlots().All(match.Cards.HasPicked))
                    {
                        BackToWaitingLocked(room);
                    }

                    return null;

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
    // it is lost if no character is left in it, a level's turn starts if the player was the
    // last one it waited for, and the room waits again if everyone left has a card.
    private void AfterLeavingMatchLocked(Room room)
    {
        switch (room.Match)
        {
            case { IsOver: false, IsLost: true } match:
                EndLocked(room, match, MatchRules.MissionsTeam);
                break;

            case { IsOver: false } match when match.EveryoneReady(MembersOf(room)):
                StartTurnLocked(room, match);
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

    private void StartTurnLocked(Room room, Match match)
    {
        match.Disarm();
        var slot = match.StartTurn();
        SendToRoomLocked(room, MatchRules.TurnOf(slot, round: 0));
        LogTurnStarted(logger, room.Number, match.Levels.Current, slot);
    }

    // Ends the match with winningTeam the winner: everyone still in the room is sent the
    // rewards, then the result, which builds the result screen from them, and the room
    // waits for the cards.
    private void EndLocked(Room room, Match match, byte winningTeam)
    {
        // The wait for the cards runs from before anyone hears of the end.
        match.End();
        match.Arm(time.CreateTimer(_ => ResultWaitElapsed(room, match), null, matchOptions.Value.ResultWait, Timeout.InfiniteTimeSpan));
        room.ClearReady();
        SendToRoomLocked(room, MatchRules.RewardsOf(room.CharacterIds()));
        SendToRoomLocked(room, new PcGameSessionEndGame(MatchRules.ResultOf(winningTeam, room.Levels()), EndGameMode: 0));
        LogMatchEnded(logger, room.Number, winningTeam == MatchRules.PlayersTeam ? "cleared" : "lost");
    }

    private void BackToWaitingLocked(Room room)
    {
        room.EndMatch();
        lobbies.Tell(room.ChannelId, new PcLobbyUpdateRoomList([room.Listing()]));
        LogWaitingAgain(logger, room.Number);
    }

    private void LevelWaitElapsed(Room room, Match match, byte level)
    {
        lock (_lock)
        {
            if (room.Match != match || !match.AwaitsTurn || match.Levels.Current != level)
            {
                return;
            }

            var late = match.NotReady(MembersOf(room));
            LogLevelWaitElapsed(logger, room.Number, level, string.Join(", ", late.Select(member => member.Id)));
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
    private static partial void LogMatchStarted(ILogger logger, ushort roomNumber, ushort missionId, byte levels);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: level {Level}'s first turn went to slot {Slot}")]
    private static partial void LogTurnStarted(ILogger logger, ushort roomNumber, byte level, byte slot);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Room {RoomNumber}: level {Level}'s first turn started without a report from connections {Connections}")]
    private static partial void LogLevelWaitElapsed(ILogger logger, ushort roomNumber, byte level, string connections);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: connection {ConnectionId} gave up its match")]
    private static partial void LogGaveUp(ILogger logger, ushort roomNumber, long connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}'s match ended: the mission was {Outcome}")]
    private static partial void LogMatchEnded(ILogger logger, ushort roomNumber, string outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber} waits for players again")]
    private static partial void LogWaitingAgain(ILogger logger, ushort roomNumber);
}
