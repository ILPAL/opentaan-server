namespace OpenTaan.Protocol.Rooms;

/// <summary>
/// A report the client makes on its own code, sent at the end of each request to create,
/// enter or quick-join a room.
/// </summary>
/// <remarks>
/// <para>
/// The client fills it in itself, from a number it draws at random and values derived
/// from that number. Checking it would need data from the client's own files, which this
/// repository does not hold, so it is read and kept, never checked.
/// </para>
/// <para>
/// A client with nothing to report sets only the first nine bytes; the last twelve are
/// whatever its memory held, so they differ from one request to the next.
/// </para>
/// </remarks>
/// <param name="Nonce">The number the client drew.</param>
/// <param name="Seal">A value the client derives from <see cref="Nonce"/>.</param>
/// <param name="Verdict">What the client found, encoded against <see cref="Nonce"/>.</param>
/// <param name="Field0C">Set only when the client has something to report.</param>
/// <param name="Field10">Set only when the client has something to report.</param>
/// <param name="Field14">Set only when the client has something to report.</param>
public readonly record struct IntegrityReport(uint Nonce, uint Seal, byte Verdict, uint Field0C, uint Field10, uint Field14)
{
    /// <summary>Bytes it occupies.</summary>
    internal const int Size = (5 * sizeof(uint)) + sizeof(byte);

    internal static IntegrityReport ReadFrom(ref PacketReader reader) =>
        new(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(Nonce);
        writer.WriteUInt32(Seal);
        writer.WriteByte(Verdict);
        writer.WriteUInt32(Field0C);
        writer.WriteUInt32(Field10);
        writer.WriteUInt32(Field14);
    }
}
