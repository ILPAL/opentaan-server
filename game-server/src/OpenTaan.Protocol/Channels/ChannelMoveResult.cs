namespace OpenTaan.Protocol.Channels;

/// <summary>The answer to <see cref="PqChannelMoveTo"/>.</summary>
/// <remarks>
/// The client also treats 2 as <see cref="Entered"/>; nothing shows what tells the two
/// apart, so this server sends only 0. Any value not named here is ignored by the client,
/// which then never leaves its channel screen.
/// </remarks>
public enum ChannelMoveResult : byte
{
    /// <summary>
    /// The player is in the channel, on the connection it already has. The client keeps
    /// the answer's channel and goes to the lobby.
    /// </summary>
    Entered = 0,

    /// <summary>
    /// The channel is on another server. The client closes its connection, connects to
    /// the answer's address and asks to move in there.
    /// </summary>
    Reconnect = 1,

    /// <summary>Refused, with a dialog.</summary>
    Refused = 3,

    /// <summary>The channel is full.</summary>
    ChannelFull = 100,

    /// <summary>The character's level is above the channel's; the client shows the channel's range.</summary>
    LevelTooHigh = 101,

    /// <summary>The character's level is below the channel's; the client shows the channel's range.</summary>
    LevelTooLow = 102,

    /// <summary>The channel is closed.</summary>
    ChannelClosed = 103,
}
