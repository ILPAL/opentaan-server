namespace OpenTaan.Protocol.Rooms;

/// <summary>Why a slot the room is told about changed, which decides how the client draws it again.</summary>
public enum SlotChange : byte
{
    /// <summary>A character took the slot. The client updates the slot as it stands.</summary>
    Seated = 0,

    /// <summary>
    /// The slot was emptied. The client clears the slot, and the host's label on it,
    /// before drawing it again; it does that for any value but 0.
    /// </summary>
    Vacated = 1,
}
