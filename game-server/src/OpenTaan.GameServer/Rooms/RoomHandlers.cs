using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>Creating, entering, quick-joining and leaving rooms, and the lobby's view of who is in one.</summary>
/// <remarks>
/// <para>
/// The client holds one lock for all four room requests: it sends none of them while one
/// is unanswered, and only an answer frees it. So each is answered exactly once on every
/// path, refusals included. A request the server fails to act on closes the connection,
/// as a failing handler always does, which the player sees, rather than leaving room
/// buttons that never work again. The view of a room has a lock of its own, and is always
/// answered too.
/// </para>
/// <para>
/// A connection needs selected characters and a channel to be in a room. The client asks
/// for rooms only from a channel's lobby, so a request without them comes from a modified
/// client, and is refused as one asking for a room that cannot be had.
/// </para>
/// <para>
/// Each room request ends with the client's report on its own code
/// (<see cref="IntegrityReport"/>), which is not checked: checking it needs data from the
/// client's files.
/// </para>
/// </remarks>
public sealed partial class RoomHandlers(
    RoomDirectory rooms,
    ChannelDirectory channels,
    ICharacterStore characters,
    IOptions<RoomOptions> options,
    ILogger<RoomHandlers> logger)
{
    /// <summary>Creates a room with the connection as its host, or refuses.</summary>
    /// <remarks>
    /// Every refusal carries the same status, since the client shows one message for any of
    /// them; the reason goes to the log.
    /// </remarks>
    public async ValueTask CreateAsync(ClientSession session, PqGameSessionCreate request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (RoomRules.Problem(request.Room, options.Value) is { } problem)
        {
            LogCreateRefused(logger, session.Id, problem);
            session.Send(new PrGameSessionCreateFail(CreateRefusal.Refused));
            return;
        }

        var member = await MemberAsync(session, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            LogNotPlaying(logger, session.Id, nameof(PqGameSessionCreate));
            session.Send(new PrGameSessionCreateFail(CreateRefusal.Refused));
            return;
        }

        var created = rooms.Create(member, request.Room);
        if (created != RoomCreation.Created)
        {
            LogCreateRefused(logger, session.Id, created.ToString());
            session.Send(new PrGameSessionCreateFail(CreateRefusal.Refused));
        }
    }

    /// <summary>Puts the connection in the room it names, or tells it why not.</summary>
    public async ValueTask EnterAsync(ClientSession session, PqGameSessionEnter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var member = await MemberAsync(session, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            LogNotPlaying(logger, session.Id, nameof(PqGameSessionEnter));
        }

        // A connection that plays no characters has no room it could enter.
        if ((member is null ? EnterRefusal.NoSuchRoom : rooms.Enter(member, request.RoomNumber, request.Password)) is { } refusal)
        {
            LogEnterRefused(logger, session.Id, request.RoomNumber, refusal);
            session.Send(new PrGameSessionEnterFail(refusal));
        }
    }

    /// <summary>Puts the connection in whichever room suits it best, or tells it there is none.</summary>
    /// <remarks>
    /// The client only ever asks for any room, so the room number the request carries is
    /// not read: which room is the server's choice; see <see cref="RoomDirectory.QuickJoin"/>.
    /// </remarks>
    public async ValueTask QuickJoinAsync(ClientSession session, PqGameSessionQuickJoin request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var member = await MemberAsync(session, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            LogNotPlaying(logger, session.Id, nameof(PqGameSessionQuickJoin));
        }

        if ((member is null ? EnterRefusal.NoSuchRoom : rooms.QuickJoin(member)) is { } refusal)
        {
            LogQuickJoinRefused(logger, session.Id, refusal);
            session.Send(new PrGameSessionQuickJoinFail());
        }
    }

    /// <summary>Takes the connection out of its room. Always answered as left, in a room or not.</summary>
    /// <remarks>
    /// The server knows which room the connection is in, so the number the request carries
    /// is only logged, as is the reason the client gives. An answer that the player stayed
    /// would keep the client in a room the player asked to leave.
    /// </remarks>
    public ValueTask LeaveAsync(ClientSession session, PqGameSessionLeave request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var wasInRoom = rooms.Leave(session);
        LogLeft(logger, session.Id, request.RoomNumber, request.Reason, wasInRoom);
        session.Send(new PrGameSessionLeave(LeaveResult.Left));
        return ValueTask.CompletedTask;
    }

    /// <summary>Lists the characters in a room of the connection's channel, or says it is gone.</summary>
    public ValueTask RoomInfoAsync(ClientSession session, PqLobbyViewRoomSimpleInfo request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var players = channels.ChannelOf(session) is { } channelId ? rooms.PlayersIn(channelId, request.RoomNumber) : null;
        session.Send(players is null
            ? new PrLobbyViewRoomSimpleInfo(RoomInfoResult.Gone, [])
            : new PrLobbyViewRoomSimpleInfo(RoomInfoResult.Listed, players));
        return ValueTask.CompletedTask;
    }

    // The connection with its channel and the characters it plays, or null when it has
    // either none selected or no channel.
    private async ValueTask<RoomMember?> MemberAsync(ClientSession session, CancellationToken cancellationToken)
    {
        if (session.SelectedCharacterIds.Count == 0 || channels.ChannelOf(session) is not { } channelId)
        {
            return null;
        }

        var playing = await SelectedCharacters.ReadAsync(characters, session, cancellationToken).ConfigureAwait(false);
        return playing.Count == session.SelectedCharacterIds.Count
            ? new RoomMember(session, session.PlayerId, channelId, playing)
            : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} could not create a room: {Reason}")]
    private static partial void LogCreateRefused(ILogger logger, long connectionId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} could not enter room {RoomNumber}: {Refusal}")]
    private static partial void LogEnterRefused(ILogger logger, long connectionId, ushort roomNumber, EnterRefusal refusal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} found no room to quick-join: {Refusal}")]
    private static partial void LogQuickJoinRefused(ILogger logger, long connectionId, EnterRefusal refusal);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet} without selected characters or a channel; refused")]
    private static partial void LogNotPlaying(ILogger logger, long connectionId, string packet);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} left room {RoomNumber} (reason {Reason}); it was in a room: {WasInRoom}")]
    private static partial void LogLeft(ILogger logger, long connectionId, ushort roomNumber, byte reason, bool wasInRoom);
}
