namespace OpenTaan.GameServer.Channels;

/// <summary>Settings bound from the <c>Channels</c> configuration section.</summary>
/// <remarks>
/// Every channel is served by this server, on the connection the player already has:
/// moving between channels never reconnects.
/// </remarks>
public sealed class ChannelOptions
{
    public const string Section = "Channels";

    /// <summary>The group the channels are listed under, and where the client is told they are.</summary>
    public ChannelServer Server { get; set; } = new();

    /// <summary>The channels, in the order the client lists them. The first is where a lobby entry without a channel move goes.</summary>
    public IReadOnlyList<ChannelSettings> List { get; set; } = [];
}

/// <summary>The group the client lists the channels under, which it calls a server.</summary>
public sealed class ChannelServer
{
    /// <summary>The group's name, shown above its channels.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The group's type. The client uses it only to pick the group it shows first, by the
    /// player's level: 0 for levels 1 to 20, 1 for 1 to 70, 4 for 61 to 102. Types 7 to 9
    /// are arena groups, which this server does not serve yet.
    /// </summary>
    public byte Type { get; set; }

    /// <summary>
    /// The IPv4 address the client is told the channels are at. When empty, the bind
    /// address is used if it names one, and otherwise the address of the network
    /// interface this machine sends from.
    /// </summary>
    /// <remarks>
    /// A machine behind a router or in a container cannot see the address the outside
    /// world reaches it at, so that one has to be set here. The client connects to it
    /// only to move to a channel on another server, which this server never asks of it.
    /// </remarks>
    public string? PublicAddress { get; set; }

    /// <summary>The port the client is told the channels are at. When empty, the port this server listens on.</summary>
    public int? PublicPort { get; set; }
}

/// <summary>One channel.</summary>
public sealed class ChannelSettings
{
    /// <summary>The kind of channel the client treats as an ordinary one.</summary>
    public const byte NormalMode = 6;

    /// <summary>The channel's id. Neither 0 nor 65535, which the client uses to mean no channel.</summary>
    public ushort Id { get; set; }

    /// <summary>The channel's name, shown in the channel list and the lobby.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What kind of channel it is: <see cref="NormalMode"/>, or 7 to 9 for arena channels,
    /// which turn on the client's arena screens. Arena is not served yet.
    /// </summary>
    public byte Mode { get; set; } = NormalMode;

    /// <summary>How many connections the channel holds. The client will not try a full one, and the server refuses it.</summary>
    public int Capacity { get; set; }

    /// <summary>Whether the channel is closed. The client shows it as closed, and the server refuses it.</summary>
    public bool Disabled { get; set; }
}
