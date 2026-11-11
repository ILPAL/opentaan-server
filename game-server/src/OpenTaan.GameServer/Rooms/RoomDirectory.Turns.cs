using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.GamePlay;

namespace OpenTaan.GameServer.Rooms;

/// <summary>A mission's turns: shots, passes, characters walking and dying, and the waits that keep turns moving.</summary>
/// <remarks>
/// <para>
/// The clients play the game. The shooter's client works a shot out and plays it, and every
/// other client plays it back from what it sends; a client walks and places its own
/// characters, and reports their deaths. The server keeps the turns: whose turn it is, what
/// the turn's character may do, when the turn ends and who has the next, and when the match
/// is over. What a client tells of its own characters goes to every other client in the
/// match, as it came, and never back to the sender: a client sends on any shot, pass or
/// fall it is given that names one of its own slots, so a copy back would come round again.
/// </para>
/// <para>
/// A turn ends once every client has played its shot back, so that no client is still in
/// the shot when the next turn starts: the start of a turn does not wait for a shot, and a
/// death in a shot reaches the server only from the dying character's own client, as it
/// plays the shot. Two waits keep one client that stops answering from holding up the rest:
/// <see cref="MatchOptions.TurnWait"/> for a turn's shot or pass, and
/// <see cref="MatchOptions.ShotWait"/> and <see cref="MatchOptions.LongestShot"/> for a
/// shot to be played back.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory
{
    private const string NoTurn = "no turn has started";

    /// <summary>
    /// Passes the shot of the character whose turn it is to every other client in the match,
    /// and waits for every client to play it back; see <see cref="FinishShot"/>.
    /// </summary>
    /// <remarks>
    /// A shot is taken only for the turn's character, from the client that plays it, and once
    /// in a turn: any other would play on every other screen while no turn was its.
    /// </remarks>
    /// <returns>Null once passed on; otherwise why the shot was dropped, for the log.</returns>
    public string? Shoot(ClientSession session, PcGamePlayScenario shot)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(shot);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (!match.HasTurn(shot.FiringSlot, session))
            {
                return WhyNotTurn(match, session, shot.FiringSlot);
            }

            // The wait runs from before anyone else has the shot.
            match.BeginShot(time.GetUtcNow());
            ArmShotWaitLocked(room, match, matchOptions.Value.LongestShot);
            SendToRoomLocked(room, shot, except: session);
            return null;
        }
    }

    /// <summary>
    /// Records that <paramref name="session"/>'s client has played the shot back, and ends the
    /// shot once every client in the room has; see <see cref="EndShotLocked"/>.
    /// </summary>
    /// <remarks>
    /// Once the first client has finished, the others are given <see cref="MatchOptions.ShotWait"/>
    /// to finish too: they have played the shot from the moment it reached them. A client
    /// reports every shot twice, and its second report changes nothing, nor is it logged.
    /// </remarks>
    /// <returns>Null once recorded, or for a client's second report; otherwise why the report changed nothing, for the log.</returns>
    public string? FinishShot(ClientSession session, PcGamePlayEndScenario report)
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
            if (match.HasFinished(session))
            {
                return null;
            }

            if (match.Phase != MatchPhase.Shot)
            {
                return "no shot is being played";
            }

            if (report.FiringSlot != match.TurnSlot)
            {
                return $"it reports a shot of slot {report.FiringSlot}, while slot {match.TurnSlot}'s is being played";
            }

            var first = !match.AnyFinished;
            match.Finished(session);
            if (match.EveryoneFinished(MembersOf(room)))
            {
                EndShotLocked(room, match);
            }
            else if (first)
            {
                var now = time.GetUtcNow();
                var due = Min(match.ShotArrived + matchOptions.Value.LongestShot, now + matchOptions.Value.ShotWait) - now;
                ArmShotWaitLocked(room, match, due > TimeSpan.Zero ? due : TimeSpan.Zero);
            }

            return null;
        }
    }

    /// <summary>
    /// Passes the turn's character's pass to every other client in the match, and passes the
    /// turn to the next character in it.
    /// </summary>
    /// <returns>Null once passed; otherwise why the pass was dropped, for the log.</returns>
    public string? PassTurn(ClientSession session, PcGamePlayPassTurn pass)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pass);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (!match.HasTurn(pass.SlotIndex, session))
            {
                return WhyNotTurn(match, session, pass.SlotIndex);
            }

            match.Moved(pass.SlotIndex, pass.X, pass.Y);
            SendToRoomLocked(room, pass, except: session);
            NextTurnLocked(room, match, match.NextInPlay(pass.SlotIndex)!.Value);
            return null;
        }
    }

    /// <summary>Passes a step of the turn's character's walk to every other client in the match; see <see cref="Relocate"/>.</summary>
    /// <returns>Null once passed on; otherwise why the step was dropped, for the log.</returns>
    public string? Walk(ClientSession session, PcGamePlayMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        return Relocate(session, move.SlotIndex, move.X, move.Y, move);
    }

    /// <summary>Passes where the turn's character stands to every other client in the match; see <see cref="Relocate"/>.</summary>
    /// <returns>Null once passed on; otherwise why the place was dropped, for the log.</returns>
    public string? Place(ClientSession session, PcGamePlaySetPos place)
    {
        ArgumentNullException.ThrowIfNull(place);
        return Relocate(session, place.SlotIndex, place.X, place.Y, place);
    }

    /// <summary>
    /// Passes a fall to every other client in the match, and tells everyone the character is a
    /// ghost; see <see cref="Die"/>.
    /// </summary>
    /// <returns>Null once done; otherwise why the fall was dropped, for the log.</returns>
    public string? Fall(ClientSession session, PcGamePlayDeathByDrop fall)
    {
        ArgumentNullException.ThrowIfNull(fall);
        return Die(session, fall.SlotIndex, fall);
    }

    /// <summary>Tells everyone a character that died in a shot is a ghost; see <see cref="Die"/>.</summary>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? MakeGhost(ClientSession session, PqGamePlayMakeGhost request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Die(session, request.SlotIndex, passOn: null);
    }

    /// <summary>Records where a client's characters stand once it has loaded the match, for the ghosts they may become.</summary>
    /// <returns>Null once recorded; otherwise why the report was dropped, for the log.</returns>
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
            if (report.Characters.FirstOrDefault(character => !match.IsPlayedBy(character.SlotIndex, session)) is { } stranger)
            {
                return $"slot {stranger.SlotIndex} is not one of its characters";
            }

            foreach (var character in report.Characters)
            {
                match.Moved(character.SlotIndex, character.X, character.Y);
            }

            return null;
        }
    }

    // Why a shot or a pass for the character in slot is not taken from the connection.
    private static string WhyNotTurn(Match match, ClientSession session, byte slot) =>
        match.Phase == MatchPhase.Shot ? "a shot is being played"
        : match.Phase != MatchPhase.Turn ? NoTurn
        : !match.IsPlayedBy(slot, session) ? $"slot {slot} is not one of its characters"
        : match.TurnSlot != slot ? $"it is slot {match.TurnSlot}'s turn, not slot {slot}'s"
        : $"the character in slot {slot} is out of the match";

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;

    // Passes where the turn's character has got to, by a step of a walk or by a place, to every
    // other client: only during its turn, its shot included, and only from its own client.
    private string? Relocate(ClientSession session, byte slot, int x, int y, IPacket packet)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            var why = match.Phase is not (MatchPhase.Turn or MatchPhase.Shot) ? NoTurn
                : !match.IsPlayedBy(slot, session) ? $"slot {slot} is not one of its characters"
                : match.TurnSlot != slot ? $"it is slot {match.TurnSlot}'s turn, not slot {slot}'s"
                : !match.InPlay(slot) ? $"the character in slot {slot} is out of the match"
                : null;
            if (why is not null)
            {
                return why;
            }

            match.Moved(slot, x, y);
            SendToRoomLocked(room, packet, except: session);
            return null;
        }
    }

    // Takes one of the connection's characters out of the match as dead: passes passOn, the
    // fall its client reported, to every other client, tells everyone the character is a
    // ghost where it was last reported standing, and goes on without it. Only the dying
    // character's own client reports its death, once.
    private string? Die(ClientSession session, byte slot, IPacket? passOn)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (!match.IsPlayedBy(slot, session))
            {
                return $"slot {slot} is not one of its characters";
            }

            if (!match.Died(slot))
            {
                return $"the character in slot {slot} is a ghost already";
            }

            if (passOn is not null)
            {
                SendToRoomLocked(room, passOn, except: session);
            }

            SendToRoomLocked(room, MatchRules.GhostOf(slot, match.PositionOf(slot, MatchRules.StartPosition)));
            LogDied(logger, room.Number, slot);
            ContinueAfterOutLocked(room, match);
            return null;
        }
    }

    // Every client has played the shot back, or the wait for them is over, and what the shot
    // led to happens now, in this order: a cleared mission is won, even by a shot that also
    // killed the last player; a match with no character left in it is lost; a level that
    // began is announced, with no end or start of a turn, since the client changes level by
    // itself and either would break it, and its first turn goes to the next character once
    // every client has played the level's opening; otherwise the next character has the turn.
    private void EndShotLocked(Room room, Match match)
    {
        if (match.HeldClear)
        {
            EndLocked(room, match, MatchRules.PlayersTeam);
            return;
        }

        if (match.IsLost)
        {
            EndLocked(room, match, MatchRules.MissionsTeam);
            return;
        }

        var next = match.NextInPlay(match.TurnSlot!.Value)!.Value;
        if (match.HeldLevel is not null)
        {
            match.HandNextTurnTo(next);
            BeginLevelLocked(room, match);
            return;
        }

        NextTurnLocked(room, match, next);
    }

    // Ends the turn running and starts the turn of the character in slot next. The wait for
    // the new turn runs from before anyone hears of it.
    private void NextTurnLocked(Room room, Match match, byte next)
    {
        var ending = match.TurnSlot!.Value;
        match.HandTurnTo(next);
        ArmTurnWaitLocked(room, match);
        SendToRoomLocked(room, MatchRules.TurnEndOf(ending));
        SendToRoomLocked(room, MatchRules.TurnOf(next, match.TurnCount, matchOptions.Value, match.GhostMoves(MatchRules.StartPosition)));
        LogTurnPassed(logger, room.Number, ending, next);
    }

    // What a match does once a character is out of it, having given up, left or died. During
    // a shot nothing is decided until every client has played it back, since the shot may
    // kill more, but a client gone is no longer waited for. Otherwise a match with no
    // character left in it is lost, a turn whose character is out passes on, and a level's
    // first turn starts if the client gone was the last one it waited for.
    private void ContinueAfterOutLocked(Room room, Match match)
    {
        if (match.Phase == MatchPhase.Shot)
        {
            if (match.EveryoneFinished(MembersOf(room)))
            {
                EndShotLocked(room, match);
            }
        }
        else if (match.IsLost)
        {
            EndLocked(room, match, MatchRules.MissionsTeam);
        }
        else if (match.Phase == MatchPhase.Turn && !match.InPlay(match.TurnSlot!.Value))
        {
            NextTurnLocked(room, match, match.NextInPlay(match.TurnSlot.Value)!.Value);
        }
        else if (match.EveryoneReady(MembersOf(room)))
        {
            StartTurnLocked(room, match);
        }
    }

    private void ArmTurnWaitLocked(Room room, Match match)
    {
        var step = match.Step;
        match.Arm(time.CreateTimer(_ => TurnWaitElapsed(room, match, step), null, matchOptions.Value.TurnWait, Timeout.InfiniteTimeSpan));
    }

    private void ArmShotWaitLocked(Room room, Match match, TimeSpan due)
    {
        var step = match.Step;
        match.Arm(time.CreateTimer(_ => ShotWaitElapsed(room, match, step), null, due, Timeout.InfiniteTimeSpan));
    }

    // The turn's character neither shot nor passed in time: its client has stopped answering,
    // since it passes by itself well before. The server passes for it.
    private void TurnWaitElapsed(Room room, Match match, int step)
    {
        lock (_lock)
        {
            if (room.Match != match || match.Step != step)
            {
                return;
            }

            var slot = match.TurnSlot!.Value;
            LogTurnWaitElapsed(logger, room.Number, slot);
            NextTurnLocked(room, match, match.NextInPlay(slot)!.Value);
        }
    }

    // Not every client played the shot back in time: the match goes on without waiting for
    // the rest.
    private void ShotWaitElapsed(Room room, Match match, int step)
    {
        lock (_lock)
        {
            if (room.Match != match || match.Step != step)
            {
                return;
            }

            var late = match.NotFinished(MembersOf(room));
            LogShotWaitElapsed(logger, room.Number, string.Join(", ", late.Select(member => member.Id)));
            EndShotLocked(room, match);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: the turn passed from slot {From} to slot {To}")]
    private static partial void LogTurnPassed(ILogger logger, ushort roomNumber, byte from, byte to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Room {RoomNumber}: slot {Slot} neither shot nor passed in time; the server passes its turn")]
    private static partial void LogTurnWaitElapsed(ILogger logger, ushort roomNumber, byte slot);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Room {RoomNumber}: the shot ended without a report from connections {Connections}")]
    private static partial void LogShotWaitElapsed(ILogger logger, ushort roomNumber, string connections);

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: the character in slot {Slot} died")]
    private static partial void LogDied(ILogger logger, ushort roomNumber, byte slot);
}
