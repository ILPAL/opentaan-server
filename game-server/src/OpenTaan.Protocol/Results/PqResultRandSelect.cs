using System.Buffers;

namespace OpenTaan.Protocol.Results;

/// <summary>A player's time to pick a result card ran out, and the client asks for one to be picked for it.</summary>
/// <remarks>
/// The client sends it when the result screen's countdown ends for a character that has
/// picked no card, and waits on no answer: the card picked is told to everyone with
/// <see cref="PrResultSelectCard"/>, and a pick that cannot be made is not answered. It
/// never refuses one, so it is never answered with <see cref="PrResultAlreadyOpend"/>.
/// </remarks>
/// <param name="GameSlotIndex">The slot of the character a card is picked for.</param>
[WireClass("PqResult_RandSelect")]
public sealed record PqResultRandSelect(byte GameSlotIndex) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqResultRandSelect>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqResultRandSelect> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var gameSlotIndex = reader.ReadByte();
        return reader.Finish(new PqResultRandSelect(gameSlotIndex));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(GameSlotIndex);
    }
}
