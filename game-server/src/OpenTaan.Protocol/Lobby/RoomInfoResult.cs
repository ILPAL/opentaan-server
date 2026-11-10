namespace OpenTaan.Protocol.Lobby;

/// <summary>The answer to a request for the players in a room.</summary>
public enum RoomInfoResult : byte
{
    /// <summary>The room's players follow.</summary>
    Listed = 0,

    /// <summary>
    /// There is no such room. The client says it could not show the room; it does that
    /// for any value but 0.
    /// </summary>
    Gone = 1,
}
