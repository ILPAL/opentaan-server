namespace OpenTaan.Protocol.Rooms;

/// <summary>Which chances a match gives its players, as the room screen shows them.</summary>
/// <remarks>
/// The client asks for <see cref="Bonus"/> when a room plays with items and
/// <see cref="Basic"/> when it does not, whenever it creates a room or changes its
/// options. It never asks for <see cref="Card"/>.
/// </remarks>
public enum ChanceMode : byte
{
    /// <summary>The plain rules.</summary>
    Basic = 0,

    /// <summary>Card chances.</summary>
    Card = 1,

    /// <summary>A bonus item at the end of a turn.</summary>
    Bonus = 2,
}
