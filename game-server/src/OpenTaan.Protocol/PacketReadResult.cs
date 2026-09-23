using System.Diagnostics.CodeAnalysis;

namespace OpenTaan.Protocol;

/// <summary>Why a packet body was refused.</summary>
public enum PacketError
{
    /// <summary>The body was not refused.</summary>
    None,

    /// <summary>The body ended before the last field did.</summary>
    Truncated,

    /// <summary>The body carries a different packet, or a nested object carries a different class.</summary>
    WrongRegistryId,

    /// <summary>
    /// A declared count or length is larger than the rest of the body could hold. It is
    /// refused before anything is allocated for it.
    /// </summary>
    CountOutOfRange,

    /// <summary>A string's bytes are not valid in its encoding.</summary>
    InvalidText,

    /// <summary>A field holds a value its type cannot represent, such as a flag that is neither 0 nor 1.</summary>
    UnexpectedValue,

    /// <summary>The body is well formed, but carries content this server does not read yet.</summary>
    Unsupported,

    /// <summary>Bytes are left over after the last field.</summary>
    TrailingBytes,
}

/// <summary>The outcome of reading one packet body.</summary>
/// <typeparam name="TPacket">The packet the body was read as.</typeparam>
/// <param name="Packet">The packet, when the body was accepted.</param>
/// <param name="Error">Why the body was refused, when it was.</param>
public readonly record struct PacketReadResult<TPacket>(TPacket? Packet, PacketError Error)
    where TPacket : class
{
    /// <summary>Whether the body was read.</summary>
    [MemberNotNullWhen(true, nameof(Packet))]
    public bool Succeeded => Packet is not null;
}
