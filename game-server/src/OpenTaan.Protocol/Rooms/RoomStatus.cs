namespace OpenTaan.Protocol.Rooms;

/// <summary>Where a room stands, as its record carries it.</summary>
/// <remarks>
/// The client treats every status but <see cref="Waiting"/> as a room in play: it lets
/// no one in from the room list and invites no one to it. A record whose status is 0
/// holds no room at all; see <see cref="GameSessionInfo"/>.
/// </remarks>
public enum RoomStatus : byte
{
    /// <summary>Waiting for players. The only status in which the client lets anyone in, or invites anyone.</summary>
    Waiting = 1,

    /// <summary>A match is being played.</summary>
    Playing = 2,

    /// <summary>What the client's create dialog puts in the room it asks for. The room it is sent back carries the server's own status.</summary>
    Requested = 5,
}
