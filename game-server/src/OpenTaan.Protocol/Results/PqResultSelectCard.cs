using System.Buffers;

namespace OpenTaan.Protocol.Results;

/// <summary>A player picks a result card for one of its characters.</summary>
/// <remarks>
/// The client keeps the pick waiting until it is answered: with
/// <see cref="PrResultSelectCard"/>, told to everyone, when the card was free, or with
/// <see cref="PrResultAlreadyOpend"/>, to the player alone, when another took it first,
/// after which the player may pick again.
/// </remarks>
/// <param name="CardIndex">The card picked, one of <see cref="PrResultSelectCard.CardCount"/> from 0.</param>
/// <param name="GameSlotIndex">The slot of the character the card is picked for.</param>
[WireClass("PqResult_SelectCard")]
public sealed record PqResultSelectCard(byte CardIndex, byte GameSlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqResultSelectCard>.RegistryId;

    /// <summary>Reads a body the client sent. Any card and slot are read; the server decides what to make of them.</summary>
    public static PacketReadResult<PqResultSelectCard> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var cardIndex = reader.ReadByte();
        var gameSlotIndex = reader.ReadByte();
        return reader.Finish(new PqResultSelectCard(cardIndex, gameSlotIndex));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(CardIndex);
        writer.WriteByte(GameSlotIndex);
    }
}
