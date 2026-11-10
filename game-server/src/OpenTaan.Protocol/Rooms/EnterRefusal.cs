namespace OpenTaan.Protocol.Rooms;

/// <summary>Why a player was not let into a room.</summary>
/// <remarks>
/// The client has a message for each of these, and a general one for any other value,
/// which this server does not send.
/// </remarks>
public enum EnterRefusal : byte
{
    /// <summary>The player is in a room already.</summary>
    AlreadyInRoom = 3,

    /// <summary>There is no such room, or it has closed.</summary>
    NoSuchRoom = 4,

    /// <summary>The room is full.</summary>
    RoomFull = 5,

    /// <summary>The password is wrong.</summary>
    WrongPassword = 6,

    /// <summary>The room's match has started.</summary>
    AlreadyStarted = 7,

    /// <summary>The room has free slots, but none the player can take.</summary>
    SlotUnavailable = 8,
}
