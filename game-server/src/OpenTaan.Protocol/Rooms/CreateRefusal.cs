namespace OpenTaan.Protocol.Rooms;

/// <summary>Why a room was not created.</summary>
/// <remarks>
/// The client shows the same message whatever the value, except 4, on which it quits
/// the game. So this server sends only <see cref="Refused"/>, and 4 cannot be written.
/// </remarks>
public enum CreateRefusal : byte
{
    /// <summary>The room was not created.</summary>
    Refused = 1,
}
