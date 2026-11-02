namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqConfirmName"/>.</summary>
/// <remarks>
/// The client shows nothing for any other value and leaves an earlier answer standing,
/// so a name it was told was available stays available. A name the server will not
/// accept is therefore answered <see cref="Taken"/>.
/// </remarks>
public enum ConfirmNameStatus : byte
{
    /// <summary>The name can be used; the client enables character creation.</summary>
    Available = 0,

    /// <summary>The name cannot be used; the client disables character creation.</summary>
    Taken = 1,
}
