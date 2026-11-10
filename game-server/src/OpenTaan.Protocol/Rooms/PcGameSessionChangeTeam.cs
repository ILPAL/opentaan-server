using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player's characters have moved to the other team: sent to everyone in the room.</summary>
/// <remarks>
/// <para>
/// The client moves the character in each slot moved from to the slot moved to, when that
/// slot carries the player's id. It finds both slots by their index without checking it,
/// so every slot here is one of the room's, and the second move is <see cref="NoSlot"/> on
/// both sides when the player has one character: a move outside those can be neither read
/// nor built. The team is one of the two, as a slot's team always is.
/// </para>
/// </remarks>
/// <param name="PlayerId">The player whose characters move, as its slots carry it.</param>
/// <param name="Team">The team they move to: 1 or 2.</param>
/// <param name="FirstFromSlot">The slot the first character leaves.</param>
/// <param name="SecondFromSlot">The slot the second character leaves, or <see cref="NoSlot"/>.</param>
/// <param name="FirstToSlot">The slot the first character takes.</param>
/// <param name="SecondToSlot">The slot the second character takes, or <see cref="NoSlot"/>.</param>
[WireClass("PcGameSession_ChangeTeam")]
public sealed record PcGameSessionChangeTeam(
    uint PlayerId,
    byte Team,
    byte FirstFromSlot,
    byte SecondFromSlot,
    byte FirstToSlot,
    byte SecondToSlot) : IPacket
{
    /// <summary>A slot for a move there is not.</summary>
    public const byte NoSlot = 0xFF;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionChangeTeam>.RegistryId;

    /// <summary>The team they move to: 1 or 2.</summary>
    public byte Team { get; } = Team is 1 or 2
        ? Team
        : throw new ArgumentOutOfRangeException(nameof(Team), "A character is on team 1 or team 2.");

    /// <summary>The slot the first character leaves.</summary>
    public byte FirstFromSlot { get; } = IsSlot(FirstFromSlot)
        ? FirstFromSlot
        : throw new ArgumentOutOfRangeException(nameof(FirstFromSlot), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>The slot the second character leaves, or <see cref="NoSlot"/>.</summary>
    public byte SecondFromSlot { get; } = IsSecondMove(SecondFromSlot, SecondToSlot)
        ? SecondFromSlot
        : throw new ArgumentOutOfRangeException(nameof(SecondFromSlot), "A second move is two of the room's slots, or none on both sides.");

    /// <summary>The slot the first character takes.</summary>
    public byte FirstToSlot { get; } = IsSlot(FirstToSlot)
        ? FirstToSlot
        : throw new ArgumentOutOfRangeException(nameof(FirstToSlot), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>The slot the second character takes, or <see cref="NoSlot"/>.</summary>
    public byte SecondToSlot { get; } = IsSecondMove(SecondFromSlot, SecondToSlot)
        ? SecondToSlot
        : throw new ArgumentOutOfRangeException(nameof(SecondToSlot), "A second move is two of the room's slots, or none on both sides.");

    /// <summary>
    /// The move of the player's characters from the slots in <paramref name="from"/> to
    /// those in <paramref name="to"/>, the first to the first and the second to the second.
    /// </summary>
    /// <exception cref="ArgumentException">The two do not each name one or two slots.</exception>
    public static PcGameSessionChangeTeam Moving(uint playerId, byte team, IReadOnlyList<byte> from, IReadOnlyList<byte> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Count != to.Count || from.Count is not (1 or 2))
        {
            throw new ArgumentException("A player moves one or two characters, each from a slot to a slot.", nameof(to));
        }

        return new PcGameSessionChangeTeam(
            playerId,
            team,
            from[0],
            from.Count == 2 ? from[1] : NoSlot,
            to[0],
            to.Count == 2 ? to[1] : NoSlot);
    }

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionChangeTeam> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var playerId = reader.ReadUInt32();
        var team = reader.ReadByte();
        var firstFrom = reader.ReadByte();
        var secondFrom = reader.ReadByte();
        var firstTo = reader.ReadByte();
        var secondTo = reader.ReadByte();
        if (team is not (1 or 2) || !IsSlot(firstFrom) || !IsSlot(firstTo) || !IsSecondMove(secondFrom, secondTo))
        {
            reader.Fail(PacketError.UnexpectedValue);
            return reader.Finish(new PcGameSessionChangeTeam(playerId, 1, 0, NoSlot, 0, NoSlot));
        }

        return reader.Finish(new PcGameSessionChangeTeam(playerId, team, firstFrom, secondFrom, firstTo, secondTo));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(PlayerId);
        writer.WriteByte(Team);
        writer.WriteByte(FirstFromSlot);
        writer.WriteByte(SecondFromSlot);
        writer.WriteByte(FirstToSlot);
        writer.WriteByte(SecondToSlot);
    }

    private static bool IsSlot(byte slot) => slot < GameSlotInfo.SlotCount;

    private static bool IsSecondMove(byte from, byte to) =>
        (IsSlot(from) && IsSlot(to)) || (from == NoSlot && to == NoSlot);
}
