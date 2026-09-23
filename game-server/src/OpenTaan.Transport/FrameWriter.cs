using System.Buffers.Binary;

namespace OpenTaan.Transport;

/// <summary>Writes frames into a send buffer.</summary>
public static class FrameWriter
{
    /// <summary>Bytes a body of <paramref name="bodyLength"/> occupies once framed.</summary>
    public static int FrameLength(int bodyLength, uint seed) =>
        bodyLength + (seed == OpenTaanCipher.PlaintextSeed
            ? TransportLimits.PlaintextOverhead
            : TransportLimits.EncryptedOverhead);

    /// <summary>
    /// Frames <paramref name="body"/> into <paramref name="destination"/> and returns
    /// the number of bytes written.
    /// </summary>
    /// <remarks>
    /// A body past the limit is refused here rather than sent. The client would not
    /// report such a frame: it would wait for a frame that cannot fit its window, in
    /// silence, until something else closed the connection.
    /// </remarks>
    public static int Write(ReadOnlySpan<byte> body, uint seed, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(body.Length, TransportLimits.MaxBody(seed), nameof(body));

        var frameLength = FrameLength(body.Length, seed);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, frameLength, nameof(destination));

        if (seed == OpenTaanCipher.PlaintextSeed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)body.Length);
            body.CopyTo(destination.Slice(TransportLimits.HeaderSize));
            return frameLength;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, OpenTaanCipher.EncodeHeader(seed, body.Length));

        var checksum = OpenTaanCipher.Encrypt(body, destination.Slice(TransportLimits.HeaderSize, body.Length), seed);

        BinaryPrimitives.WriteUInt32LittleEndian(
            destination.Slice(TransportLimits.HeaderSize + body.Length, TransportLimits.ChecksumSize),
            OpenTaanCipher.MaskChecksum(seed, checksum));

        return frameLength;
    }
}
