using System.Globalization;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Data;

namespace OpenTaan.GameServer.Channels;

/// <summary>Whether a connection got into the channel it asked for.</summary>
public enum ChannelJoin
{
    /// <summary>It is in the channel, or already was.</summary>
    Joined,

    /// <summary>No channel has that id.</summary>
    Unknown,

    /// <summary>The channel is closed.</summary>
    Closed,

    /// <summary>The channel holds as many connections as it can.</summary>
    Full,
}

/// <summary>The configured channels, and which channel each connection is in. Shared by every connection.</summary>
/// <remarks>
/// A connection is in one channel at most, from the move that put it there until it moves
/// again or closes. Checking that a channel has room and taking the place are one step
/// under one lock, so two connections cannot both take its last place.
/// </remarks>
public sealed class ChannelDirectory
{
    private readonly IReadOnlyList<ChannelSettings> _channels;
    private readonly string _serverName;
    private readonly byte _serverType;
    private readonly Dictionary<ClientSession, ushort> _members = [];
    private readonly Dictionary<ushort, int> _occupancy;
    private readonly Lock _lock = new();

    /// <summary>Takes the channels from <paramref name="options"/>, which must list at least one.</summary>
    public ChannelDirectory(ChannelOptions options, AdvertisedAddress address)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfZero(options.List.Count, nameof(options));

        _channels = options.List;
        _serverName = options.Server.Name;
        _serverType = options.Server.Type;
        _occupancy = _channels.ToDictionary(channel => channel.Id, _ => 0);
        Address = address;
    }

    /// <summary>Where the client is told the channels are.</summary>
    public AdvertisedAddress Address { get; }

    /// <summary>The channel a lobby entry goes to when the connection has not moved to one.</summary>
    public ushort FirstChannelId => _channels[0].Id;

    /// <summary>
    /// The channel list as the client reads it: a root holding the group, which holds one
    /// node per channel, each described by its attributes.
    /// </summary>
    public DataNode ListTree()
    {
        var ip = Address.Address.ToString();
        var port = Text(Address.Port);

        lock (_lock)
        {
            var channels = _channels
                .Select(channel => DataNode.Element(
                    "channel",
                    [
                        new("id", Text(channel.Id)),
                        new("name", channel.Name),
                        new("ip", ip),
                        new("port", port),
                        // No channel has a level range: 0 is none.
                        new("maxLevel", "0"),
                        new("minLevel", "0"),
                        new("fill", Text(FillPercent(channel))),
                        new("disable", channel.Disabled ? "1" : "0"),
                        new("mode", Text(channel.Mode)),
                    ],
                    []))
                .ToArray();

            var server = DataNode.Element("server", [new("name", _serverName), new("type", Text(_serverType))], channels);
            return DataNode.Element(string.Empty, [], [server]);
        }
    }

    /// <summary>The channel as the client keeps the one it is in, or null when no channel has that id.</summary>
    public ChannelInfo? Describe(ushort channelId)
    {
        var channel = Find(channelId);
        if (channel is null)
        {
            return null;
        }

        lock (_lock)
        {
            return new ChannelInfo(
                channel.Name,
                channel.Id,
                channel.Mode,
                Address.ToWire(),
                FillPercent(channel),
                Field1C: 0,
                MinimumLevel: 0,
                MaximumLevel: 0,
                Disabled: channel.Disabled ? (byte)1 : (byte)0);
        }
    }

    /// <summary>Moves <paramref name="session"/> into a channel, leaving the one it was in.</summary>
    public ChannelJoin TryJoin(ClientSession session, ushort channelId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var channel = Find(channelId);
        if (channel is null)
        {
            return ChannelJoin.Unknown;
        }

        if (channel.Disabled)
        {
            return ChannelJoin.Closed;
        }

        lock (_lock)
        {
            if (_members.TryGetValue(session, out var current) && current == channelId)
            {
                return ChannelJoin.Joined;
            }

            if (_occupancy[channelId] >= channel.Capacity)
            {
                return ChannelJoin.Full;
            }

            LeaveLocked(session);
            _members[session] = channelId;
            _occupancy[channelId]++;
            return ChannelJoin.Joined;
        }
    }

    /// <summary>The mode of channel <paramref name="channelId"/>, or null when no channel has that id.</summary>
    public byte? ModeOf(ushort channelId) => Find(channelId)?.Mode;

    /// <summary>The mode of the channel <paramref name="session"/> is in, or null when it is in none.</summary>
    public byte? ModeOf(ClientSession session) => ChannelOf(session) is { } channelId ? ModeOf(channelId) : null;

    /// <summary>The channel <paramref name="session"/> is in, or null.</summary>
    public ushort? ChannelOf(ClientSession session)
    {
        lock (_lock)
        {
            return _members.TryGetValue(session, out var channelId) ? channelId : null;
        }
    }

    /// <summary>Takes <paramref name="session"/> out of its channel, if it is in one.</summary>
    public void Leave(ClientSession session)
    {
        lock (_lock)
        {
            LeaveLocked(session);
        }
    }

    private void LeaveLocked(ClientSession session)
    {
        if (_members.Remove(session, out var channelId))
        {
            _occupancy[channelId]--;
        }
    }

    private ChannelSettings? Find(ushort channelId) => _channels.FirstOrDefault(channel => channel.Id == channelId);

    // At 100 or more the client calls the channel full.
    private uint FillPercent(ChannelSettings channel) => (uint)(_occupancy[channel.Id] * 100 / channel.Capacity);

    private static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
