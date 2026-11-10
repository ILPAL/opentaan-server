namespace OpenTaan.Protocol.Rooms;

/// <summary>What one of a room's slots holds.</summary>
public enum SlotStatus : byte
{
    /// <summary>Empty, and open to anyone entering.</summary>
    Open = 0,

    /// <summary>Empty, and closed by the host.</summary>
    Closed = 1,

    /// <summary>A character, not ready to start.</summary>
    Seated = 2,

    /// <summary>A character, ready to start.</summary>
    Ready = 3,
}
