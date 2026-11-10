namespace OpenTaan.GameServer.Rooms;

/// <summary>Hands out room numbers. Not safe to share between threads: its owner's lock guards it.</summary>
/// <remarks>
/// <para>
/// Numbers run from <see cref="First"/> to <see cref="Last"/>. The client never shows a
/// room numbered 0, and a quick join asking for any room names
/// <see cref="Protocol.Rooms.PqGameSessionQuickJoin.AnyRoom"/>, the number above the last.
/// </para>
/// <para>
/// They are handed out in rising order, wrapping after the last and passing over those in
/// use, rather than the lowest free one first. A number freed is then not given to the
/// next room at once, so a player acting on a room that has just closed, from a lobby
/// list not yet told, does not land in a new room under the old one's number.
/// </para>
/// </remarks>
public sealed class RoomNumbers
{
    /// <summary>The first number handed out.</summary>
    public const ushort First = 1;

    /// <summary>The last number handed out.</summary>
    public const ushort Last = 0xFFFE;

    private ushort _next = First;

    /// <summary>The next number not in use, or null when every number is.</summary>
    /// <param name="isInUse">Whether a number is already a room's.</param>
    public ushort? Take(Func<ushort, bool> isInUse)
    {
        ArgumentNullException.ThrowIfNull(isInUse);

        for (var tried = 0; tried <= Last - First; tried++)
        {
            var number = _next;
            _next = number == Last ? First : (ushort)(number + 1);
            if (!isInUse(number))
            {
                return number;
            }
        }

        return null;
    }
}
