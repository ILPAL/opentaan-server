using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Results;

/// <summary>A result card has been turned over for a character: sent to everyone still in the match.</summary>
/// <remarks>
/// The client shows, on that card, the reward naming the character in the slot given, so
/// the slot is the character's own. It looks the reward up by slot before it checks the
/// slot, and the cards it lets a player pick are the first <see cref="CardCount"/>, so
/// neither value can be built outside them.
/// </remarks>
/// <param name="CardIndex">The card turned over, below <see cref="CardCount"/>.</param>
/// <param name="GameSlotIndex">The slot of the character it was turned over for, below <see cref="GameSlotInfo.SlotCount"/>.</param>
[WireClass("PrResult_SelectCard")]
public sealed record PrResultSelectCard(byte CardIndex, byte GameSlotIndex) : IPacket
{
    /// <summary>How many cards a player may pick from: the first row, numbered from 0.</summary>
    public const int CardCount = 8;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrResultSelectCard>.RegistryId;

    /// <summary>The card turned over, below <see cref="CardCount"/>.</summary>
    public byte CardIndex { get; } = CardIndex < CardCount
        ? CardIndex
        : throw new ArgumentOutOfRangeException(nameof(CardIndex), $"A player picks from cards 0 to {CardCount - 1}.");

    /// <summary>The slot of the character it was turned over for, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte GameSlotIndex { get; } = GameSlotIndex < GameSlotInfo.SlotCount
        ? GameSlotIndex
        : throw new ArgumentOutOfRangeException(nameof(GameSlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrResultSelectCard> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var cardIndex = reader.ReadByte();
        var gameSlotIndex = reader.ReadByte();
        if (cardIndex >= CardCount || gameSlotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            return new PacketReadResult<PrResultSelectCard>(null, reader.Error);
        }

        return reader.Finish(new PrResultSelectCard(cardIndex, gameSlotIndex));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(CardIndex);
        writer.WriteByte(GameSlotIndex);
    }
}
