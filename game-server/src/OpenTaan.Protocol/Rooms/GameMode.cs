namespace OpenTaan.Protocol.Rooms;

/// <summary>The rules a room's match is played by: with items or without, and with team captains or without.</summary>
public enum GameMode : ushort
{
    /// <summary>Items in play.</summary>
    Items = 0,

    /// <summary>No items.</summary>
    NoItems = 1,

    /// <summary>Items in play, and each team has a captain.</summary>
    CaptainItems = 2,

    /// <summary>No items, and each team has a captain.</summary>
    CaptainNoItems = 3,
}
