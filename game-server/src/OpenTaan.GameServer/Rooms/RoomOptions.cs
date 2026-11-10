namespace OpenTaan.GameServer.Rooms;

/// <summary>Settings bound from the <c>Rooms</c> configuration section.</summary>
/// <remarks>
/// Both are bounds against a client asking for more than the game ever needs, not
/// capacity plans: a channel's players fill far fewer rooms than the limit, and the
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

    /// <summary>Rooms one channel may hold at once. A request to create one more is refused.</summary>
    public int MaxPerChannel { get; set; } = 500;

    /// <summary>The longest room name a request to create a room may carry, in UTF-16 code units.</summary>
    public int MaxNameLength { get; set; } = 128;
}
