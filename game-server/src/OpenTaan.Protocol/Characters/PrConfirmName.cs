using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqConfirmName"/>.</summary>
/// <remarks>
/// It does not name the name it answers for, so the client applies it to whatever is
/// in the name box when it arrives. A name is checked again when the character is
/// created.
/// </remarks>
/// <param name="Status">Whether the name can be used. Only the values the enum names can be sent.</param>
[WireClass("PrConfirmName")]
public sealed record PrConfirmName(ConfirmNameStatus Status) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrConfirmName>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrConfirmName> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var status = (ConfirmNameStatus)reader.ReadByte();
        return reader.Finish(new PrConfirmName(status));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Status"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Status);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Status);
    }
}
