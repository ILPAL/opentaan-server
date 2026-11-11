using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Matches;

/// <summary>Starting a match, a mission's levels and turns, giving up, and the result screen's cards.</summary>
/// <remarks>
/// The client waits on none of these but a result card a player picks itself, which is
/// answered either way. So a request the match's rules do not allow is dropped, and only
/// logged; see <see cref="RoomDirectory.StartMatch"/>, <see cref="RoomDirectory.Shoot"/> and
/// the methods after them for the rules.
/// </remarks>
public sealed partial class MatchHandlers(RoomDirectory rooms, ILogger<MatchHandlers> logger)
{
    /// <summary>Starts the host's match; see <see cref="RoomDirectory.StartMatch"/>.</summary>
    public ValueTask StartGameAsync(ClientSession session, PqGameSessionStartGame request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqGameSessionStartGame), rooms.StartMatch(session));
        return ValueTask.CompletedTask;
    }

    /// <summary>Begins the mission level asked for, or clears the mission; see <see cref="RoomDirectory.AskForLevel"/>.</summary>
    public ValueTask StartMissionLevelAsync(ClientSession session, PqGameSessionStartMissionLevel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqGameSessionStartMissionLevel), rooms.AskForLevel(session, request.Level));
        return ValueTask.CompletedTask;
    }

    /// <summary>Records a mission level reported ready; see <see cref="RoomDirectory.ReportLevelReady"/>.</summary>
    public ValueTask MissionLevelReadyAsync(ClientSession session, PqGameSessionMissionLevelReady request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqGameSessionMissionLevelReady), rooms.ReportLevelReady(session, request.Level));
        return ValueTask.CompletedTask;
    }

    /// <summary>Takes the player's characters out of its match; see <see cref="RoomDirectory.GiveUp"/>.</summary>
    public ValueTask GiveUpAsync(ClientSession session, PcGamePlayGiveUp request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PcGamePlayGiveUp), rooms.GiveUp(session));
        return ValueTask.CompletedTask;
    }

    /// <summary>Records where the client's characters stand once loaded; see <see cref="RoomDirectory.ReportLoaded"/>.</summary>
    public ValueTask LoadingDoneAsync(ClientSession session, PcGamePlayLoadingDone report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);

        LogUnless(session, nameof(PcGamePlayLoadingDone), rooms.ReportLoaded(session, report));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes the turn's shot to the other clients; see <see cref="RoomDirectory.Shoot"/>.</summary>
    public ValueTask ScenarioAsync(ClientSession session, PcGamePlayScenario shot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(shot);

        LogUnless(session, nameof(PcGamePlayScenario), rooms.Shoot(session, shot));
        return ValueTask.CompletedTask;
    }

    /// <summary>Records a shot played back; see <see cref="RoomDirectory.FinishShot"/>.</summary>
    public ValueTask EndScenarioAsync(ClientSession session, PcGamePlayEndScenario report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);

        LogUnless(session, nameof(PcGamePlayEndScenario), rooms.FinishShot(session, report));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes the turn on; see <see cref="RoomDirectory.PassTurn"/>.</summary>
    public ValueTask PassTurnAsync(ClientSession session, PcGamePlayPassTurn pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pass);

        LogUnless(session, nameof(PcGamePlayPassTurn), rooms.PassTurn(session, pass));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes a step of the turn's character's walk on; see <see cref="RoomDirectory.Walk"/>.</summary>
    public ValueTask MoveAsync(ClientSession session, PcGamePlayMove move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(move);

        LogUnless(session, nameof(PcGamePlayMove), rooms.Walk(session, move));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes where the turn's character stands on; see <see cref="RoomDirectory.Place"/>.</summary>
    public ValueTask SetPosAsync(ClientSession session, PcGamePlaySetPos place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(place);

        LogUnless(session, nameof(PcGamePlaySetPos), rooms.Place(session, place));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes a character's fall on and makes it a ghost; see <see cref="RoomDirectory.Fall"/>.</summary>
    public ValueTask DeathByDropAsync(ClientSession session, PcGamePlayDeathByDrop fall, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fall);

        LogUnless(session, nameof(PcGamePlayDeathByDrop), rooms.Fall(session, fall));
        return ValueTask.CompletedTask;
    }

    /// <summary>Makes a character that died in a shot a ghost; see <see cref="RoomDirectory.MakeGhost"/>.</summary>
    public ValueTask MakeGhostAsync(ClientSession session, PqGamePlayMakeGhost request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqGamePlayMakeGhost), rooms.MakeGhost(session, request));
        return ValueTask.CompletedTask;
    }

    /// <summary>Picks one of the cards left for a player whose time ran out; see <see cref="RoomDirectory.PickCard"/>.</summary>
    public ValueTask RandSelectAsync(ClientSession session, PqResultRandSelect request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqResultRandSelect), rooms.PickCard(session, request.GameSlotIndex, card: null));
        return ValueTask.CompletedTask;
    }

    /// <summary>Turns over the card a player picked; see <see cref="RoomDirectory.PickCard"/>.</summary>
    public ValueTask SelectCardAsync(ClientSession session, PqResultSelectCard request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqResultSelectCard), rooms.PickCard(session, request.GameSlotIndex, request.CardIndex));
        return ValueTask.CompletedTask;
    }

    // Logs why a request about a match changed nothing, when it did not.
    private void LogUnless(ClientSession session, string packet, string? reason)
    {
        if (reason is not null)
        {
            LogChangedNothing(logger, session.Id, packet, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} sent {Packet}, which changed nothing: {Reason}")]
    private static partial void LogChangedNothing(ILogger logger, long connectionId, string packet, string reason);
}
