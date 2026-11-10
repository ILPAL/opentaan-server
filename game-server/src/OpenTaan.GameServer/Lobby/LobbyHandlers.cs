using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Lobby;

namespace OpenTaan.GameServer.Lobby;

/// <summary>Entering and leaving a channel's lobby, and the character pictures the client sends from it.</summary>
/// <remarks>
/// Who is in each lobby is kept by <see cref="LobbyRoster"/>, and the rooms each lobby
/// lists by <see cref="RoomDirectory"/>.
/// </remarks>
public sealed partial class LobbyHandlers(
    ChannelDirectory channels,
    LobbyRoster lobbies,
    RoomDirectory rooms,
    ICharacterStore characters,
    IOptions<PortraitOptions> portraits,
    ILogger<LobbyHandlers> logger)
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Enters the lobby of the connection's channel, and lists its rooms and the players in it.</summary>
    /// <remarks>
    /// <para>
    /// A connection that has not moved to a channel enters the first one, as long as it
    /// could have moved there. An entry before characters are selected is refused: the
    /// client then goes back to its channel screen rather than waiting.
    /// </para>
    /// <para>
    /// A client coming back to its lobby from a room says it left the room first. A
    /// connection still in a room is taken out of it here, so no room keeps a slot for a
    /// player who is no longer there.
    /// </para>
    /// </remarks>
    public async ValueTask EnterAsync(ClientSession session, PqLobbyEnter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (session.SelectedCharacterIds.Count == 0)
        {
            LogEntryBeforeSelection(logger, session.Id);
            session.Send(new PrLobbyEnter(0, LobbyEnterResult.Failed));
            return;
        }

        if (rooms.Leave(session))
        {
            LogEntryFromRoom(logger, session.Id);
        }

        var channelId = channels.ChannelOf(session) ?? channels.FirstChannelId;
        var join = channels.TryJoin(session, channelId);
        if (join != ChannelJoin.Joined)
        {
            LogEntryRefused(logger, session.Id, channelId, join);
            session.Send(new PrLobbyEnter(channelId, LobbyEnterResult.Failed));
            return;
        }

        var playing = await SelectedCharacters.ReadAsync(characters, session, cancellationToken).ConfigureAwait(false);

        session.Send(new PrLobbyEnter(channelId, LobbyEnterResult.Entered));
        rooms.EnterLobby(session, channelId, playing);
    }

    /// <summary>
    /// Leaves the lobby, which everyone left in it is told. Always answered, in a lobby or
    /// not: until it is, the client cannot enter a lobby again.
    /// </summary>
    /// <remarks>
    /// The client also says it is leaving its lobby once it has been sent a room it
    /// entered, so a connection in a room stays there.
    /// </remarks>
    public ValueTask LeaveAsync(ClientSession session, PqLobbyLeave request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        lobbies.Leave(session);
        session.Send(new PrLobbyLeave());
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Keeps the picture the client sent of one of its selected characters, when a folder
    /// is set for them. Nothing is answered.
    /// </summary>
    /// <remarks>
    /// The file is named by the character's id alone, so nothing the client sends reaches
    /// the path. A picture that is not a PNG file is kept apart, under another extension,
    /// so it can be looked at without being taken for one.
    /// </remarks>
    public async ValueTask UploadPortraitAsync(ClientSession session, PcUploadWebImage upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(upload);

        if (!session.SelectedCharacterIds.Contains(upload.CharacterId))
        {
            LogPortraitNotSelected(logger, session.Id, upload.CharacterId);
            return;
        }

        if (upload.Image is not { } image)
        {
            LogPortraitEmpty(logger, session.Id, upload.CharacterId);
            return;
        }

        var isPng = image.AsSpan().StartsWith(PngSignature);
        if (!isPng)
        {
            LogPortraitNotPng(logger, session.Id, upload.CharacterId, image.Length);
        }

        if (string.IsNullOrEmpty(portraits.Value.Folder))
        {
            return;
        }

        Directory.CreateDirectory(portraits.Value.Folder);
        var path = Path.Combine(portraits.Value.Folder, $"{upload.CharacterId}{(isPng ? ".png" : ".bin")}");
        await File.WriteAllBytesAsync(path, image, cancellationToken).ConfigureAwait(false);
        LogPortraitKept(logger, session.Id, upload.CharacterId, image.Length, path);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} asked to enter a lobby before selecting characters; refused")]
    private static partial void LogEntryBeforeSelection(ILogger logger, long connectionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} entered a lobby without leaving its room first; took it out of the room")]
    private static partial void LogEntryFromRoom(ILogger logger, long connectionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId} could not enter the lobby of channel {ChannelId}: {Join}")]
    private static partial void LogEntryRefused(ILogger logger, long connectionId, ushort channelId, ChannelJoin join);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a picture of character {CharacterId}, which it has not selected; dropped")]
    private static partial void LogPortraitNotSelected(ILogger logger, long connectionId, uint characterId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} sent no picture of character {CharacterId}")]
    private static partial void LogPortraitEmpty(ILogger logger, long connectionId, uint characterId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a picture of character {CharacterId} that is not a PNG file ({Length} bytes)")]
    private static partial void LogPortraitNotPng(ILogger logger, long connectionId, uint characterId, int length);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId} sent a picture of character {CharacterId}, {Length} bytes, kept at {Path}")]
    private static partial void LogPortraitKept(ILogger logger, long connectionId, uint characterId, int length, string path);
}
