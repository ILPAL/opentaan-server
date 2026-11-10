namespace OpenTaan.Protocol.Rooms;

/// <summary>The kind of match a room plays. The client's create dialog offers these two.</summary>
public enum GameType : byte
{
    /// <summary>Two teams of players, one against the other.</summary>
    TeamGame = 0,

    /// <summary>
    /// Players together against the game's own enemies. The players sit on one side,
    /// in slots 0, 1, 4 and 5, and the client lets such a room use only
    /// <see cref="GameMode.Items"/>.
    /// </summary>
    Mission = 1,
}
