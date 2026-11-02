using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenTaan.Protocol.Data;

/// <summary>
/// The envelope the client wraps variable-length data in: its data files, the channel
/// list, mail and bingo cards, and the chat history a player report carries.
/// </summary>
/// <remarks>
/// <para>
/// Little-endian: the byte <c>S</c>, a flags byte, the <see cref="ZeroStartAdler32"/>
/// checksum of the contents, then, only when the flags say so, a scramble key and then
/// the contents' size, then the payload. Flag bit 0 means the payload is zlib-compressed,
/// bit 1 that it is scrambled with the key; the client ignores the other bits, and so
/// does this. A payload is compressed first and scrambled second, and unwrapped in the
/// opposite order. The checksum is always of the contents, never of the payload.
/// </para>
/// <para>
/// The client calls the scrambling encryption, but the key travels with the data, so it
/// hides nothing. It is supported because envelopes the client builds may use it.
/// </para>
/// <para>
/// Unwrapping trusts nothing it reads. The declared size is checked against the caller's
/// limit before anything is allocated for it, decompression stops at that size, and the
/// checksum must match. A refused envelope is reported, never thrown.
/// </para>
/// </remarks>
public static class DataEnvelope
{
    private const byte Signature = (byte)'S';
    private const byte CompressedFlag = 0x01;
    private const byte ScrambledFlag = 0x02;
    private const int HeaderSize = sizeof(byte) + sizeof(byte) + sizeof(uint);

    // The client's keystream: sixteen words, the first derived from the key and each
    // next one a fixed step below the last, repeated over the payload.
    private const uint KeySeedMask = 0x8473FBC1;
    private const uint KeyStep = 2072773695;
    private const int KeyWords = 16;

    /// <summary>Wraps <paramref name="contents"/> in an envelope.</summary>
    /// <param name="contents">What the envelope carries.</param>
    /// <param name="compress">
    /// Whether to compress, which happens only when it saves more than the four bytes the
    /// size field then costs, as the client decides. Off by default: the envelopes the
    /// client is sent are not compressed.
    /// </param>
    /// <param name="scrambleKey">The key to scramble the payload with, or 0 to leave it as it is.</param>
    public static byte[] Wrap(ReadOnlySpan<byte> contents, bool compress = false, uint scrambleKey = 0)
    {
        byte flags = 0;
        byte[] payload = contents.ToArray();

        if (compress && Compress(contents) is var compressed && compressed.Length + sizeof(uint) < contents.Length)
        {
            flags |= CompressedFlag;
            payload = compressed;
        }

        if (scrambleKey != 0)
        {
            flags |= ScrambledFlag;
            Scramble(payload, scrambleKey);
        }

        var header = new List<byte>(HeaderSize + (2 * sizeof(uint))) { Signature, flags };
        header.AddRange(LittleEndian(ZeroStartAdler32.Of(contents)));
        if ((flags & ScrambledFlag) != 0)
        {
            header.AddRange(LittleEndian(scrambleKey));
        }

        if ((flags & CompressedFlag) != 0)
        {
            header.AddRange(LittleEndian((uint)contents.Length));
        }

        return [.. header, .. payload];
    }

    /// <summary>Unwraps an envelope, refusing contents larger than <paramref name="maximumSize"/>.</summary>
    /// <param name="envelope">The envelope, from its signature to its last byte.</param>
    /// <param name="maximumSize">The most bytes the contents may have. Anything a client sent needs a limit.</param>
    /// <param name="contents">The contents, or empty when the envelope is refused.</param>
    /// <returns><see cref="PacketError.None"/>, or why the envelope was refused.</returns>
    public static PacketError TryUnwrap(ReadOnlySpan<byte> envelope, int maximumSize, out byte[] contents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSize);
        contents = [];

        if (envelope.Length < HeaderSize)
        {
            return PacketError.Truncated;
        }

        if (envelope[0] != Signature)
        {
            return PacketError.UnexpectedValue;
        }

        var flags = envelope[1];
        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(envelope[2..]);
        var position = HeaderSize;

        uint scrambleKey = 0;
        if ((flags & ScrambledFlag) != 0)
        {
            if (envelope.Length < position + sizeof(uint))
            {
                return PacketError.Truncated;
            }

            scrambleKey = BinaryPrimitives.ReadUInt32LittleEndian(envelope[position..]);
            position += sizeof(uint);
        }

        uint? declaredSize = null;
        if ((flags & CompressedFlag) != 0)
        {
            if (envelope.Length < position + sizeof(uint))
            {
                return PacketError.Truncated;
            }

            declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(envelope[position..]);
            position += sizeof(uint);
            if (declaredSize > (uint)maximumSize)
            {
                return PacketError.CountOutOfRange;
            }
        }

        var payload = envelope[position..].ToArray();
        if ((flags & ScrambledFlag) != 0)
        {
            Scramble(payload, scrambleKey);
        }

        byte[] unwrapped;
        if (declaredSize is { } size)
        {
            if (!TryDecompress(payload, (int)size, out unwrapped))
            {
                return PacketError.UnexpectedValue;
            }
        }
        else if (payload.Length > maximumSize)
        {
            return PacketError.CountOutOfRange;
        }
        else
        {
            unwrapped = payload;
        }

        if (ZeroStartAdler32.Of(unwrapped) != checksum)
        {
            return PacketError.UnexpectedValue;
        }

        contents = unwrapped;
        return PacketError.None;
    }

    private static byte[] Compress(ReadOnlySpan<byte> contents)
    {
        using var compressed = new MemoryStream();
        using (var deflater = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflater.Write(contents);
        }

        return compressed.ToArray();
    }

    // Exactly `size` bytes, or false: a stream that is corrupt, ends early, or goes on
    // past its declared size is refused, and nothing beyond `size` is ever produced.
    private static bool TryDecompress(byte[] payload, int size, out byte[] contents)
    {
        contents = [];
        try
        {
            using var inflater = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress);
            var buffer = new byte[size];
            var filled = 0;
            while (filled < size)
            {
                var read = inflater.Read(buffer, filled, size - filled);
                if (read == 0)
                {
                    return false;
                }

                filled += read;
            }

            Span<byte> beyond = stackalloc byte[1];
            if (inflater.Read(beyond) != 0)
            {
                return false;
            }

            contents = buffer;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // Scrambling and unscrambling are the same operation: each byte is XORed with the
    // keystream byte at its position, the keystream repeating every 64 bytes.
    private static void Scramble(Span<byte> payload, uint key)
    {
        Span<byte> keystream = stackalloc byte[KeyWords * sizeof(uint)];
        var word = key ^ KeySeedMask;
        for (var index = 0; index < KeyWords; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(keystream[(index * sizeof(uint))..], word);
            word -= KeyStep;
        }

        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] ^= keystream[index % keystream.Length];
        }
    }

    private static byte[] LittleEndian(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
