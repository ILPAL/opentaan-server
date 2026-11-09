namespace OpenTaan.GameServer;

/// <summary>Settings bound from the <c>GameServer</c> configuration section.</summary>
public sealed class GameServerOptions
{
    public const string Section = "GameServer";

    /// <summary>Address the TCP listener binds to.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Port game clients connect to.</summary>
    public int Port { get; set; } = 2180;

    /// <summary>
    /// Connections held open at once. A connection past it is closed at once, which the
    /// client shows as a failure to connect rather than a server that never answers.
    /// </summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>
    /// Connections held open at once from one address. One player holds more than one
    /// at times, and players behind one router share an address, so this bounds a
    /// flood from one machine rather than counting players.
    /// </summary>
    public int MaxConnectionsPerAddress { get; set; } = 32;

    /// <summary>
    /// How long a connection may go without sending a frame. The client sends a
    /// heartbeat every 60 seconds on every connection, so the default is three
    /// heartbeats' worth of silence before the connection counts as gone.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(180);

    /// <summary>
    /// How long one frame may take to leave. A client that stops reading is closed
    /// once this passes, rather than holding its replies, and its memory, forever.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Replies that may wait to be sent on one connection. Filling it means the client
    /// has stopped reading; the connection is closed rather than the sender blocked.
    /// </summary>
    public int SendQueueCapacity { get; set; } = 256;

    /// <summary>
    /// Optional JSON file naming packets by registry id, as <c>{"ids": {"Name": id}}</c>,
    /// so that logs name a packet this server does not implement instead of printing
    /// its id. A relative path is looked for in the content root and each folder above
    /// it. Nothing depends on it being there.
    /// </summary>
    public string? PacketNamesFile { get; set; }

    /// <summary>
    /// Logs a packet this server has no handler for and drops it, instead of closing the
    /// connection. For finding out, in one run of a real client, everything a screen
    /// sends. Allowed only in the Development environment: a request dropped this way is
    /// never answered, and the client waits for it forever.
    /// </summary>
    public bool DropUnhandledPackets { get; set; }
}
