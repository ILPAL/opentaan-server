namespace OpenTaan.Protocol.Rooms;

/// <summary>The answer to a request to leave a room.</summary>
public enum LeaveResult : byte
{
    /// <summary>The player stays in the room, and the client goes on showing it.</summary>
    Stayed = 0,

    /// <summary>
    /// The player has left. The client forgets the room and goes back to the lobby; it
    /// does that for any value but 0.
    /// </summary>
    Left = 1,
}
