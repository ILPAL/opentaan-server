using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Channels;

namespace OpenTaan.GameServer.Channels;

/// <summary>Listing the channels and moving between them.</summary>
/// <remarks>
/// Every channel is on this server, so a move is answered as entered and the player stays
/// on its connection. The client then goes to the channel's lobby.
/// </remarks>
public sealed partial class ChannelHandlers(ChannelDirectory channels, ILogger<ChannelHandlers> logger)
{
    /// <summary>Sends the channel list, with how full each channel is now.</summary>
    public ValueTask GetChannelListAsync(ClientSession session, PqGetChannelList request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.Send(new PrGetChannelList(channels.ListTree()));
        return ValueTask.CompletedTask;
    }

    /// <summary>Moves the connection into a channel, or tells the client why not.</summary>
    /// <remarks>
    /// <para>
    /// The client opens its channel screen only once characters are selected, so a move
    /// before that comes from a modified client. No answer describes it, so the connection
    /// is closed.
    /// </para>
    /// <para>
    /// The client also asks to move when its player follows a friend or a guild member:
    /// it then names no channel and passes the person to follow. Following needs friends
    /// and guilds, which do not exist yet, so such a move is refused like any move to a
    /// channel that does not exist: as closed, which the client shows and gets over.
    /// </para>
    /// </remarks>
    public ValueTask MoveToAsync(ClientSession session, PqChannelMoveTo request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (session.SelectedCharacterIds.Count == 0)
        {
            LogMoveBeforeSelection(logger, session.Id);
            session.Close(CloseReason.NoCharacterSelected);
            return ValueTask.CompletedTask;
        }

        if (request.Context != 0)
        {
            LogFollow(logger, session.Id, request.ChannelId, request.Context);
        }

        var result = channels.TryJoin(session, request.ChannelId) switch
        {
            ChannelJoin.Joined => ChannelMoveResult.Entered,
            ChannelJoin.Full => ChannelMoveResult.ChannelFull,
            _ => ChannelMoveResult.ChannelClosed,
        };

        if (result == ChannelMoveResult.Entered)
        {
            LogEntered(logger, session.Id, request.ChannelId);
        }

        session.Send(new PrChannelMoveTo(
            result,
            request.ChannelId,
            channels.Address.ToWire(),
            Characters: [],
            Channel: result == ChannelMoveResult.Entered ? channels.Describe(request.ChannelId) : null));
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} asked to move to a channel before selecting characters; closing it")]
    private static partial void LogMoveBeforeSelection(ILogger logger, long connectionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId} asked to move to channel {ChannelId} following {Context}, which is not served yet")]
    private static partial void LogFollow(ILogger logger, long connectionId, ushort channelId, uint context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} entered channel {ChannelId}")]
    private static partial void LogEntered(ILogger logger, long connectionId, ushort channelId);
}
