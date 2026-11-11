using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Matches;

/// <summary>Starting a match, a mission's levels, giving up, and the result screen's cards.</summary>
/// <remarks>
/// The client waits on none of these but a result card a player picks itself, which is
/// answered either way. So a request the match's rules do not allow is dropped, and only
/// logged; see <see cref="RoomDirectory.StartMatch"/> and the methods after it for the
/// rules.
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
