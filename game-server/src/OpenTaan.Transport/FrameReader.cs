using System.Buffers.Binary;

namespace OpenTaan.Transport;

/// <summary>
/// Reads frames from the front of a receive buffer. Holds no state of its own: the
/// caller owns the buffer and the seed, and advances both.
/// </summary>
public static class FrameReader
{
    /// <summary>
    /// Reads at most one frame from the front of <paramref name="input"/>.
    /// </summary>
    /// <param name="input">Bytes received so far, starting at a frame boundary.</param>
    /// <param name="seed">
    /// The receive seed for this frame. <see cref="OpenTaanCipher.PlaintextSeed"/> selects
    /// the unencrypted framing, in which the header is the body length itself and no
    /// checksum follows.
    /// </param>
    /// <param name="destination">
    /// Where the body is written. Must have room for <see cref="TransportLimits.MaxEncryptedBody"/>
    /// bytes, so that a frame is never refused for a reason the sender cannot observe.
    /// </param>
    /// <param name="verifyChecksum">
    /// Whether to check the trailing checksum. Only meaningful for encrypted frames.
    /// </param>
    /// <remarks>
    /// A declared length is checked before anything is reserved for it, and an
    /// impossible one is refused immediately rather than waiting for bytes that will
    /// never arrive: answering <see cref="FrameStatus.Incomplete"/> to a four-gigabyte
    /// length would leave the connection buffering until it was killed from outside.
    /// </remarks>
    public static FrameReadResult TryRead(
        ReadOnlySpan<byte> input,
        uint seed,
        Span<byte> destination,
        bool verifyChecksum = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, TransportLimits.MaxEncryptedBody);

        if (input.Length < TransportLimits.HeaderSize)
        {
            return new FrameReadResult(FrameStatus.Incomplete, 0, 0);
        }

        var header = BinaryPrimitives.ReadUInt32LittleEndian(input);

        return seed == OpenTaanCipher.PlaintextSeed
            ? ReadPlaintext(input, header, destination)
            : ReadEncrypted(input, header, seed, destination, verifyChecksum);
    }

    private static FrameReadResult ReadPlaintext(ReadOnlySpan<byte> input, uint header, Span<byte> destination)
    {
        // Unencrypted framing carries the body length in the clear and has no trailer.
        if (header > TransportLimits.MaxPlaintextBody)
        {
            return new FrameReadResult(FrameStatus.Invalid, 0, 0, FrameError.DeclaredLengthOutOfRange);
        }

        var bodyLength = (int)header;
        var frameLength = TransportLimits.HeaderSize + bodyLength;
        if (input.Length < frameLength)
        {
            return new FrameReadResult(FrameStatus.Incomplete, 0, 0);
        }

        input.Slice(TransportLimits.HeaderSize, bodyLength).CopyTo(destination);
        return new FrameReadResult(FrameStatus.Complete, frameLength, bodyLength);
    }

    private static FrameReadResult ReadEncrypted(
        ReadOnlySpan<byte> input,
        uint header,
        uint seed,
        Span<byte> destination,
        bool verifyChecksum)
    {
        if (!OpenTaanCipher.TryDecodeBodyLength(header, seed, out var bodyLength))
        {
            return new FrameReadResult(FrameStatus.Invalid, 0, 0, FrameError.DeclaredLengthOutOfRange);
        }

        var frameLength = TransportLimits.HeaderSize + bodyLength + TransportLimits.ChecksumSize;
        if (input.Length < frameLength)
        {
            return new FrameReadResult(FrameStatus.Incomplete, 0, 0);
        }

        var body = input.Slice(TransportLimits.HeaderSize, bodyLength);
        var checksum = OpenTaanCipher.Decrypt(body, destination, seed);

        if (verifyChecksum)
        {
            var trailer = BinaryPrimitives.ReadUInt32LittleEndian(
                input.Slice(TransportLimits.HeaderSize + bodyLength, TransportLimits.ChecksumSize));

            if (OpenTaanCipher.MaskChecksum(seed, trailer) != checksum)
            {
                return new FrameReadResult(FrameStatus.Invalid, 0, 0, FrameError.ChecksumMismatch);
            }
        }

        return new FrameReadResult(FrameStatus.Complete, frameLength, bodyLength);
    }
}
