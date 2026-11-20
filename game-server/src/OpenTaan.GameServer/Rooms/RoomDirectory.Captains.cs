using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>Choosing a room's captains, in the modes with captains, before its match starts.</summary>
/// <remarks>
/// <para>
/// In a room playing <see cref="GameMode.CaptainItems"/> or <see cref="GameMode.CaptainNoItems"/>
/// (modes 2 and 3), the host's client asks for the captains to be chosen instead of a start,
/// once every other player in the room is ready. Everyone in the room is told, and every
/// client shows the choice for five seconds: its player's team, each character with a
/// light, and for each of the player's own characters a button to volunteer as captain and
/// one to withdraw. The server keeps no time of its own: the host's client asks for the
/// start when its five seconds are over, and the choice lasts until the match starts or the
/// room's game mode changes.
/// </para>
/// <para>
/// As the match starts, each team's captain is drawn evenly from the team's characters
/// that volunteer, or from all of its characters when none does, so every team with a
/// character has one; a team with none, as a match may start with, has no captain. The
/// client gives a captain an extra shot with every shot, and on a captain's turn lets its
/// teammates give it their items, so one captain for each team keeps the match even. The
/// client takes the captains from the start alone, so a team whose captain's player leaves
/// plays on without one.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory
{
    /// <summary>
    /// Starts choosing the captains of the host's room, as its host asks, and tells everyone
    /// in it, the host included.
    /// </summary>
    /// <remarks>
    /// Only the host of a waiting room playing a mode with captains asks. Any volunteering
    /// from before is forgotten, as every client opens the choice afresh with every light
    /// out. Whether the other players are ready is left to the start, which checks it.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? ChooseCaptains(ClientSession session)
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
                return "only the host asks for the captains";
            }

            if (!MatchRules.HasCaptains(room.GameMode))
            {
                return $"the room plays {room.GameMode}, which has no captains";
            }

            room.ChooseCaptains();
            SendToRoomLocked(room, new PcGameSessionChiefDecision());
            return null;
        }
    }

    /// <summary>
    /// Has one of a player's characters volunteer as its team's captain, and tells everyone
    /// in the room, the player included.
    /// </summary>
    /// <remarks>
    /// Only one of the player's own characters volunteers, while its waiting room chooses
    /// its captains. Any other is not taken, and the player alone is told so, which leaves
    /// the character's light out.
    /// </remarks>
    /// <returns>Null once done; otherwise why the volunteering was not taken, for the log.</returns>
    public string? Volunteer(ClientSession session, uint characterId)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (CaptainChoiceProblemLocked(room, session, characterId) is { } problem)
            {
                session.Send(new PcGameSessionFailChallengeChief(characterId));
                return problem;
            }

            room.Volunteer(characterId);
            SendToRoomLocked(room, new PcGameSessionChallengeChief(characterId));
            return null;
        }
    }

    /// <summary>
    /// Takes back one of a player's characters' volunteering as its team's captain, and
    /// tells everyone in the room, the player included.
    /// </summary>
    /// <remarks>
    /// Only one of the player's own characters that volunteers withdraws, while its waiting
    /// room chooses its captains.
    /// </remarks>
    /// <returns>Null once done; otherwise why the request was dropped, for the log.</returns>
    public string? Withdraw(ClientSession session, uint characterId)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            if (WaitingRoomLocked(session) is not { } room)
            {
                return NotWaiting;
            }

            if (CaptainChoiceProblemLocked(room, session, characterId) is { } problem)
            {
                return problem;
            }

            if (!room.Volunteers(characterId))
            {
                return $"character {characterId} does not volunteer";
            }

            room.Withdraw(characterId);
            SendToRoomLocked(room, new PcGameSessionCancelChief(characterId));
            return null;
        }
    }

    // Why the player may not volunteer or withdraw the character in the room: the room is
    // not choosing its captains, or the character is not one of the player's own.
    private static string? CaptainChoiceProblemLocked(Room room, ClientSession session, uint characterId) =>
        !room.IsChoosingCaptains ? "the room is not choosing its captains"
        : room.SlotOf(characterId) is not { } slot || room.MemberIn(slot).Session != session ? $"character {characterId} is not one of the player's own in its room"
        : null;

    // For each team number, the slot of the team's captain, drawn evenly from its pool: the
    // first entry, for no team, names none, and nor does a team's with no character, whose
    // pool is empty.
    private byte[] DrawCaptainsLocked(Room room)
    {
        var captains = PcGameSessionStartGame.NoCaptains.ToArray();
        for (byte team = 1; team < captains.Length; team++)
        {
            var pool = room.CaptainPoolOf(team);
            if (pool.Count > 0)
            {
                captains[team] = pool[random.Next(pool.Count)];
            }
        }

        LogCaptainsDrawn(logger, room.Number, captains[1], captains[2]);
        return captains;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Room {RoomNumber}: slot {FirstCaptain} captains the first team, and slot {SecondCaptain} the second")]
    private static partial void LogCaptainsDrawn(ILogger logger, ushort roomNumber, byte firstCaptain, byte secondCaptain);
}
