using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
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

    /// <summary>The maps a host may pick in a room of teams in a channel of mode <paramref name="channelMode"/>: none when the mode has no list.</summary>
    public IReadOnlyList<ushort> MapsFor(byte channelMode) => MapsByChannelMode.GetValueOrDefault(channelMode) ?? [];

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

/// <summary>Refuses room settings that leave the hosts in a channel with no map to pick.</summary>
public sealed class RoomMapsValidation(IOptions<ChannelOptions> channels) : IValidateOptions<RoomOptions>
{
    public ValidateOptionsResult Validate(string? name, RoomOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var missing = channels.Value.List
            .Select(channel => channel.Mode)
            .Distinct()
            .Where(mode => options.MapsFor(mode).Count == 0)
            .Order()
            .ToArray();

        return missing.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{RoomOptions.Section}:{nameof(RoomOptions.MapsByChannelMode)} lists no maps for channel mode " +
                $"{string.Join(", ", missing)}, which {ChannelOptions.Section}:List uses: a host there could never pick a map");
    }
}
