using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
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
public sealed partial class MatchHandlers(
    RoomDirectory rooms,
    ChannelDirectory channels,
    IOptions<RoomOptions> roomOptions,
    ILogger<MatchHandlers> logger)
{
    /// <summary>
    /// Starts the host's match, a random pick of map drawn among the maps the client lists in
    /// the connection's channel; see <see cref="RoomDirectory.StartMatch"/>.
    /// </summary>
    public ValueTask StartGameAsync(ClientSession session, PqGameSessionStartGame request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        LogUnless(session, nameof(PqGameSessionStartGame), rooms.StartMatch(session, roomOptions.Value.MapsFor(channels.ModeOf(session))));
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

    /// <summary>Records that the client has loaded its match, and where its characters stand; see <see cref="RoomDirectory.ReportLoaded"/>.</summary>
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

    /// <summary>Passes an item used on; see <see cref="RoomDirectory.UseItem"/>.</summary>
    public ValueTask UseItemAsync(ClientSession session, PcGamePlayUseItem use, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(use);

        LogUnless(session, nameof(PcGamePlayUseItem), rooms.UseItem(session, use));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes the items a shot spent on; see <see cref="RoomDirectory.SpendItem"/>.</summary>
    public ValueTask SpendItemAsync(ClientSession session, PcGamePlaySpendItem spend, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(spend);

        LogUnless(session, nameof(PcGamePlaySpendItem), rooms.SpendItem(session, spend));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes an item thrown away on; see <see cref="RoomDirectory.ForsakeItem"/>.</summary>
    public ValueTask ForsakeItemAsync(ClientSession session, PcGamePlayForsakeItem forsake, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(forsake);

        LogUnless(session, nameof(PcGamePlayForsakeItem), rooms.ForsakeItem(session, forsake));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes an item in use taken off on; see <see cref="RoomDirectory.ForsakeActiveItem"/>.</summary>
    public ValueTask ForsakeActiveItemAsync(ClientSession session, PcGamePlayForsakeActiveItem forsake, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(forsake);

        LogUnless(session, nameof(PcGamePlayForsakeActiveItem), rooms.ForsakeActiveItem(session, forsake));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes a box a ghost caught on; see <see cref="RoomDirectory.EatItemBox"/>.</summary>
    public ValueTask EatItemBoxAsync(ClientSession session, PcGamePlayEatItemBox caught, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(caught);

        LogUnless(session, nameof(PcGamePlayEatItemBox), rooms.EatItemBox(session, caught));
        return ValueTask.CompletedTask;
    }

    /// <summary>Passes a bonus item on; see <see cref="RoomDirectory.EatBonusItem"/>.</summary>
    public ValueTask EatBonusItemAsync(ClientSession session, PcGamePlayEatBonusItem bonus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bonus);

        LogUnless(session, nameof(PcGamePlayEatBonusItem), rooms.EatBonusItem(session, bonus));
        return ValueTask.CompletedTask;
    }

    /// <summary>Sends where an item went back to its sender; see <see cref="RoomDirectory.NotifyEatItem"/>.</summary>
    public ValueTask NotifyEatItemAsync(ClientSession session, PcGamePlayNotifyEatItem notify, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(notify);

        LogUnless(session, nameof(PcGamePlayNotifyEatItem), rooms.NotifyEatItem(session, notify));
        return ValueTask.CompletedTask;
    }

    /// <summary>Records and passes on a ghost's target; see <see cref="RoomDirectory.AimGhost"/>.</summary>
    public ValueTask SetGhostTargetAsync(ClientSession session, PcGamePlaySetGhostTarget aim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aim);

        LogUnless(session, nameof(PcGamePlaySetGhostTarget), rooms.AimGhost(session, aim));
        return ValueTask.CompletedTask;
    }

    /// <summary>Records and passes on where a ghost stopped; see <see cref="RoomDirectory.PlaceGhost"/>.</summary>
    public ValueTask SetGhostPosAsync(ClientSession session, PcGamePlaySetGhostPos place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(place);

        LogUnless(session, nameof(PcGamePlaySetGhostPos), rooms.PlaceGhost(session, place));
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
