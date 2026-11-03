namespace OpenTaan.GameServer.Sessions;

/// <summary>Why a connection ended. Only the first reason is kept.</summary>
public enum CloseReason
{
    /// <summary>The client closed the connection.</summary>
    PeerClosed,

    /// <summary>No frame arrived for longer than the idle timeout.</summary>
    IdleTimeout,

    /// <summary>
    /// A frame could not be read: an impossible length or a wrong checksum. The seeds
    /// no longer agree, and there is no way back in step.
    /// </summary>
    InvalidFrame,

    /// <summary>A packet this server handles arrived with a body its codec refused.</summary>
    MalformedPacket,

    /// <summary>
    /// A packet arrived that this server has no handler for. The client never
    /// re-sends a request, so a request left unanswered would stay lost, and some
    /// screens stay locked until their reply arrives. A closed connection is at least
    /// something the player sees.
    /// </summary>
    UnhandledPacket,

    /// <summary>A login arrived on a connection that had already logged in.</summary>
    SecondLogin,

    /// <summary>
    /// A selection named characters the connection's accounts do not have, or a number
    /// of them no real client sends. Its answer has no way to refuse.
    /// </summary>
    InvalidSelection,

    /// <summary>
    /// A request that needs selected characters arrived before any were, which no real
    /// client sends, and its answer has no way to refuse it.
    /// </summary>
    NoCharacterSelected,

    /// <summary>A reply was too large to fit the client's receive window.</summary>
    OversizedReply,

    /// <summary>Replies piled up faster than the client read them.</summary>
    SendQueueFull,

    /// <summary>A frame took longer than the send timeout to leave.</summary>
    SendTimeout,

    /// <summary>The socket failed.</summary>
    TransportError,

    /// <summary>A handler failed. The request it was serving cannot be answered.</summary>
    ServerError,

    /// <summary>The server is shutting down.</summary>
    ServerStopping,
}
