using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Matches;

namespace OpenTaan.GameServer.Rooms;

/// <summary>A mission's enemies' turn, which follows every shot in a level whose enemies take turns.</summary>
/// <remarks>
/// <para>
/// Mission enemies never attack. In their turn each client moves its own copy of the
/// level's enemies, each by an amount drawn from the match's seed, places the level's next
/// wave of enemies if it has one, and lets the damage over time the enemies carry take its
/// toll. Every client moves them alike only if every client is sent every turn of theirs,
/// once: a client that missed one, or was sent one twice, would have its enemies stand
/// elsewhere, its shots hit other enemies, and its count of the enemies killed, by which
/// it asks for the next level, drift from the others'. So the enemies' turn goes to every
/// connection in the match once, however many characters it plays, the players out of the
/// match included, as their clients still show the field.
/// </para>
/// <para>
/// The shooter's turn ends first, so that no character has the turn while the enemies take
/// theirs, and the next turn starts once every client in the match has reported the
/// enemies' turn over on its screen. Two waits keep one client that stops answering from
/// holding up the rest: <see cref="MatchOptions.EnemyTurnWait"/> once the first client has
/// reported, and <see cref="MatchOptions.LongestEnemyTurn"/> in all.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory
{
    /// <summary>
    /// Records that <paramref name="session"/>'s client reports the enemies' turn over on its
    /// screen, and starts the next turn once every client in the room has.
    /// </summary>
    /// <remarks>
    /// Once the first client has reported, the others are given
    /// <see cref="MatchOptions.EnemyTurnWait"/> to report too: they have moved the same
    /// enemies from the moment the turn reached them. A report counts only for the enemies'
    /// turn running, once for each connection.
    /// </remarks>
    /// <returns>Null once recorded; otherwise why the report changed nothing, for the log.</returns>
    public string? FinishEnemyTurn(ClientSession session, PcGameSessionFinishMissionEnemyTurn report)
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
            var first = !match.AnyAnswered;
            if (!match.Answered(session, report.Counter, out var reason))
            {
                return reason;
            }

            if (match.EveryoneAnswered(MembersOf(room)))
            {
                EndEnemyTurnLocked(room, match);
            }
            else if (first)
            {
                var now = time.GetUtcNow();
                var due = Min(match.EnemyTurnSent + matchOptions.Value.LongestEnemyTurn, now + matchOptions.Value.EnemyTurnWait) - now;
                ArmEnemyTurnWaitLocked(room, match, due > TimeSpan.Zero ? due : TimeSpan.Zero);
            }

            return null;
        }
    }

    // Ends the shooter's turn and has the enemies take theirs, on every client in the match.
    // The wait for the clients' reports runs from before anyone hears of it.
    private void BeginEnemyTurnLocked(Room room, Match match)
    {
        var counter = match.BeginEnemyTurn(time.GetUtcNow());
        ArmEnemyTurnWaitLocked(room, match, matchOptions.Value.LongestEnemyTurn);
        SendToRoomLocked(room, MatchRules.TurnEndOf(match.TurnSlot!.Value));
        SendToRoomLocked(room, new PqGameSessionStartMissionEnemyTurn(counter));
        LogEnemyTurnBegan(logger, room.Number, counter);
    }

    // The enemies' turn is over on every client, or the wait for them is: the character
    // after the shooter has the turn. The wait for it runs from before anyone hears of it.
    private void EndEnemyTurnLocked(Room room, Match match)
    {
        var shooter = match.TurnSlot!.Value;
        var next = match.NextInPlay(shooter)!.Value;
        match.HandTurnTo(next);
        ArmTurnWaitLocked(room, match);
        SendTurnStartLocked(room, match, next);
        LogTurnPassed(logger, room.Number, shooter, next);
    }

    private void ArmEnemyTurnWaitLocked(Room room, Match match, TimeSpan due)
    {
        var step = match.Step;
        match.Arm(time.CreateTimer(_ => EnemyTurnWaitElapsed(room, match, step), null, due, Timeout.InfiniteTimeSpan));
    }

    // Not every client reported the enemies' turn over in time: the match goes on without
    // waiting for the rest.
    private void EnemyTurnWaitElapsed(Room room, Match match, int step)
    {
        lock (_lock)
        {
            if (room.Match != match || match.Step != step)
            {
                return;
            }

            var late = match.NotAnswered(MembersOf(room));
            LogEnemyTurnWaitElapsed(logger, room.Number, string.Join(", ", late.Select(member => member.Id)));
            EndEnemyTurnLocked(room, match);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomNumber}: the enemies took turn {Counter}")]
    private static partial void LogEnemyTurnBegan(ILogger logger, ushort roomNumber, uint counter);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Room {RoomNumber}: the enemies' turn ended without a report from connections {Connections}")]
    private static partial void LogEnemyTurnWaitElapsed(ILogger logger, ushort roomNumber, string connections);
}
