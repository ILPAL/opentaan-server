namespace OpenTaan.Protocol.Rooms;

/// <summary>The rules a room's match is played by: with items or without, and with team captains or without.</summary>
/// <remarks>
/// Item boxes drop in every mode. With items, a box caught or a bonus at the end of a turn
/// gives the character an item. Without them, it fills the character's team gauge instead,
/// as hitting well does, and the gauge's grade gives the team's shots their bonuses.
/// </remarks>
public enum GameMode : ushort
{
    /// <summary>Items in play.</summary>
    Items = 0,

    /// <summary>No items: a team gauge instead.</summary>
    NoItems = 1,

    /// <summary>Items in play, and each team has a captain.</summary>
    CaptainItems = 2,

    /// <summary>No items, a team gauge instead, and each team has a captain.</summary>
    CaptainNoItems = 3,
}
