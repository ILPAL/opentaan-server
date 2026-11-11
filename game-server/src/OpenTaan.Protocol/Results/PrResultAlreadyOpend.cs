using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Results;

/// <summary>The card a player picked was taken first: sent to that player alone, who may then pick another.</summary>
/// <remarks>
/// Named, as on the wire, "Opend". The client takes the pick it is waiting on off its
/// queue without checking that one is there, so this answers only a player's own
/// <see cref="PqResultSelectCard"/>, never a pick made for it with
/// <see cref="PqResultRandSelect"/>.
/// </remarks>
/// <param name="CardIndex">The card that was taken, below <see cref="PrResultSelectCard.CardCount"/>.</param>
/// <param name="GameSlotIndex">The slot of the character the player picked for, below <see cref="GameSlotInfo.SlotCount"/>.</param>
[WireClass("PrResult_AlreadyOpend")]
public sealed record PrResultAlreadyOpend(byte CardIndex, byte GameSlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrResultAlreadyOpend>.RegistryId;

    /// <summary>The card that was taken, below <see cref="PrResultSelectCard.CardCount"/>.</summary>
    public byte CardIndex { get; } = CardIndex < PrResultSelectCard.CardCount
        ? CardIndex
        : throw new ArgumentOutOfRangeException(nameof(CardIndex), $"A player picks from cards 0 to {PrResultSelectCard.CardCount - 1}.");

    /// <summary>The slot of the character the player picked for, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte GameSlotIndex { get; } = GameSlotIndex < GameSlotInfo.SlotCount
        ? GameSlotIndex
        : throw new ArgumentOutOfRangeException(nameof(GameSlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrResultAlreadyOpend> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var cardIndex = reader.ReadByte();
        var gameSlotIndex = reader.ReadByte();
        if (cardIndex >= PrResultSelectCard.CardCount || gameSlotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            return new PacketReadResult<PrResultAlreadyOpend>(null, reader.Error);
        }

        return reader.Finish(new PrResultAlreadyOpend(cardIndex, gameSlotIndex));
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
