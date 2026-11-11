using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Matches;

namespace OpenTaan.GameServer.Rooms;

/// <summary>Settings bound from the <c>Rooms</c> configuration section.</summary>
/// <remarks>
/// The two limits are bounds against a client asking for more than the game ever needs,
/// not capacity plans: a channel's players fill far fewer rooms than the limit, and the
/// client's own name field is far shorter.
/// </remarks>
public sealed class RoomOptions
{
    public const string Section = "Rooms";

    /// <summary>
    /// The highest <see cref="MaxNameLength"/> allowed. A room reply carries the name once
    /// and eight slots at a few hundred bytes each, so a name this long still fits a frame
    /// many times over.
    /// </summary>
    public const int LongestNameAllowed = 1000;

    /// <summary>The most levels a mission can have: the client asks for the one after its last with a value of its own.</summary>
    public const byte MaxMissionLevels = PqGameSessionStartMissionLevel.NoNextLevel - 1;

    /// <summary>Rooms one channel may hold at once. A request to create one more is refused.</summary>
    public int MaxPerChannel { get; set; } = 500;

    /// <summary>The longest room name a request to create a room may carry, in UTF-16 code units.</summary>
    public int MaxNameLength { get; set; } = 128;

    /// <summary>
    /// The maps the host of a room of teams may pick, by the mode of the room's channel: the
    /// ones the client's map list lets a host pick in that mode, its random picks included.
    /// </summary>
    /// <remarks>
    /// The client takes its map list from its own files, and greys out each map the mode of
    /// the player's channel does not allow, so a host asking for another map is using a
    /// modified client, and is refused. Every channel's mode needs a list. A mission room's
    /// host picks a mission instead, which no list here covers.
    /// </remarks>
    public IReadOnlyDictionary<byte, IReadOnlyList<ushort>> MapsByChannelMode { get; set; } = new Dictionary<byte, IReadOnlyList<ushort>>();

    /// <summary>
    /// The maps a host may pick in a room of teams in a channel of mode <paramref name="channelMode"/>:
    /// none when the mode has no list, or when there is no channel.
    /// </summary>
    public IReadOnlyList<ushort> MapsFor(byte? channelMode) =>
        channelMode is { } mode ? MapsByChannelMode.GetValueOrDefault(mode) ?? [] : [];

    /// <summary>
    /// For each of the client's random picks, the maps of the pick's group, from which its
    /// match's map is drawn, along with <see cref="RandomMapExtras"/>.
    /// </summary>
    /// <remarks>
    /// The client lists a random pick as a map of its own, but it cannot load one: a match
    /// started on a random pick would leave every client in the room without a map. So a
    /// match on one is played on a map drawn at its start, evenly, from the pick's group
    /// among the maps the room's channel offers, and the extras; see <see cref="RandomPool"/>.
    /// The room still shows the pick, and its next match draws again. The groups are the
    /// client's own, from its map table.
    /// </remarks>
    public IReadOnlyDictionary<ushort, IReadOnlyList<ushort>> RandomMaps { get; set; } = new Dictionary<ushort, IReadOnlyList<ushort>>();

    /// <summary>
    /// Maps every random pick may also be played on, whatever the room's channel offers: maps
    /// the client can load but never lists for a host to pick.
    /// </summary>
    public IReadOnlyList<ushort> RandomMapExtras { get; set; } = [];

    /// <summary>
    /// The maps random pick <paramref name="pick"/> draws from in a channel offering
    /// <paramref name="maps"/>: the maps of its group the channel offers, in the group's order,
    /// then the extras, each once. Empty for a map with no group.
    /// </summary>
    public IReadOnlyList<ushort> RandomPool(ushort pick, IReadOnlyCollection<ushort> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);

        return RandomMaps.TryGetValue(pick, out var group)
            ? [.. group.Where(maps.Contains).Concat(RandomMapExtras).Distinct()]
            : [];
    }

    /// <summary>
    /// Why the random picks cannot be used in channels of <paramref name="channelModes"/>, for
    /// the log, or null when they can: a random pick a mode offers must have a map to draw
    /// from there, and only random picks have groups, which, like the extras, hold only maps
    /// the client can load.
    /// </summary>
    public string? RandomMapsProblem(IEnumerable<byte> channelModes)
    {
        ArgumentNullException.ThrowIfNull(channelModes);

        var strangers = RandomMaps.Keys.Where(map => !MatchRules.RandomMapPicks.Contains(map)).Order().ToArray();
        if (strangers.Length > 0)
        {
            return $"{Section}:{nameof(RandomMaps)} gives a group to map {string.Join(", ", strangers)}: only the client's random picks, " +
                   $"{string.Join(", ", MatchRules.RandomMapPicks.Order())}, have one";
        }

        if (RandomMaps.Values.Append(RandomMapExtras).SelectMany(maps => maps).Any(MatchRules.RandomMapPicks.Contains))
        {
            return $"{Section}:{nameof(RandomMaps)} and {Section}:{nameof(RandomMapExtras)} must hold only maps the client can load, never a random pick";
        }

        foreach (var mode in channelModes.Distinct().Order())
        {
            var maps = MapsFor(mode);
            var empty = maps.Where(map => MatchRules.RandomMapPicks.Contains(map) && RandomPool(map, maps).Count == 0).Order().ToArray();
            if (empty.Length > 0)
            {
                return $"{Section}:{nameof(RandomMaps)} leaves random pick {string.Join(", ", empty)}, which channel mode {mode} offers, no map to draw from";
            }
        }

        return null;
    }

    /// <summary>
    /// The missions a mission room may play, by the mission's own number, each with how many
    /// levels it has.
    /// </summary>
    /// <remarks>
    /// The client takes its missions from its own files, and a match started with a mission
    /// it does not have breaks every client in the room, so a host asking for another is
    /// refused, and no match starts with one. The levels bound what a client may ask for: the
    /// mission is cleared only once its last level runs. The list must hold
    /// <see cref="Room.FirstMission"/>, which every new mission room plays until its host
    /// picks another.
    /// </remarks>
    public IReadOnlyDictionary<ushort, byte> Missions { get; set; } = new Dictionary<ushort, byte>();

    /// <summary>Why <see cref="Missions"/> cannot be used, for the log, or null when it can.</summary>
    public string? MissionsProblem()
    {
        if (!Missions.ContainsKey(Room.FirstMission))
        {
            return $"{Section}:{nameof(Missions)} must list mission {Room.FirstMission}, which every new mission room plays until its host picks another";
        }

        return Missions.Values.All(levels => levels is >= 1 and <= MaxMissionLevels)
            ? null
            : $"{Section}:{nameof(Missions)} must give each mission 1 to {MaxMissionLevels} levels";
    }
}

/// <summary>
/// Refuses room settings that leave the hosts in a channel with no map to pick, or with a
/// random pick that has no map to draw from.
/// </summary>
public sealed class RoomMapsValidation(IOptions<ChannelOptions> channels) : IValidateOptions<RoomOptions>
{
    public ValidateOptionsResult Validate(string? name, RoomOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var modes = channels.Value.List.Select(channel => channel.Mode).Distinct().Order().ToArray();
        var missing = modes.Where(mode => options.MapsFor(mode).Count == 0).ToArray();
        if (missing.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"{RoomOptions.Section}:{nameof(RoomOptions.MapsByChannelMode)} lists no maps for channel mode " +
                $"{string.Join(", ", missing)}, which {ChannelOptions.Section}:List uses: a host there could never pick a map");
        }

        return options.RandomMapsProblem(modes) is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }
}
