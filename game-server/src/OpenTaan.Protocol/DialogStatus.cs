namespace OpenTaan.Protocol;

/// <summary>
/// Guards the statuses of answers the client shows as dialogs. For a value it has no
/// dialog for, the client shows nothing and keeps waiting, which a player cannot tell
/// from no answer at all.
/// </summary>
internal static class DialogStatus
{
    /// <summary>Refuses a status the enum does not name, since the client has no dialog for it.</summary>
    /// <exception cref="InvalidOperationException">The status is not one the client knows.</exception>
    public static void EnsureKnown<TStatus>(TStatus status)
        where TStatus : struct, Enum
    {
        if (!Enum.IsDefined(status))
        {
            throw new InvalidOperationException($"{typeof(TStatus).Name} {status} has no dialog on the client.");
        }
    }
}
