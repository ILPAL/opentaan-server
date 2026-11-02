using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace OpenTaan.Protocol;

/// <summary>
/// Writes the fields of a packet body, front to back, little-endian. The counterpart
/// of <see cref="PacketReader"/>.
/// </summary>
/// <remarks>
/// A value that cannot be written as the client would read it is a bug in this server,
/// not bad input, so it throws rather than writing something else.
/// </remarks>
public readonly struct PacketWriter
{
    private static readonly UnicodeEncoding Utf16 = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    private readonly IBufferWriter<byte> _destination;

    /// <summary>Writes to the end of <paramref name="destination"/>.</summary>
    public PacketWriter(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _destination = destination;
    }

    /// <summary>Writes an unsigned 8-bit value.</summary>
    public void WriteByte(byte value)
    {
        _destination.GetSpan(sizeof(byte))[0] = value;
        _destination.Advance(sizeof(byte));
    }

    /// <summary>Writes an unsigned 16-bit value.</summary>
    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_destination.GetSpan(sizeof(ushort)), value);
        _destination.Advance(sizeof(ushort));
    }

    /// <summary>Writes an unsigned 32-bit value.</summary>
    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_destination.GetSpan(sizeof(uint)), value);
        _destination.Advance(sizeof(uint));
    }

    /// <summary>Writes an unsigned 64-bit value.</summary>
    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_destination.GetSpan(sizeof(ulong)), value);
        _destination.Advance(sizeof(ulong));
    }

    /// <summary>Writes a one-byte flag: 1 for true, 0 for false.</summary>
    public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Writes a 32-bit count of UTF-16 code units, then the units, little-endian.</summary>
    /// <exception cref="EncoderFallbackException">The string holds a broken surrogate pair.</exception>
    public void WriteUtf16String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        WriteUInt32((uint)value.Length);
        var written = Utf16.GetBytes(value, _destination.GetSpan(value.Length * sizeof(char)));
        _destination.Advance(written);
    }

    /// <summary>Writes a 32-bit count of bytes, then one byte per character.</summary>
    /// <exception cref="ArgumentException">A character is above U+00FF, so it has no single byte.</exception>
    public void WriteByteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Latin-1 would write '?' for a character it cannot hold; refuse instead.
        foreach (var character in value)
        {
            if (character > 'ÿ')
            {
                throw new ArgumentException("A single-byte string can only hold characters up to U+00FF.", nameof(value));
            }
        }

        WriteUInt32((uint)value.Length);
        var written = Encoding.Latin1.GetBytes(value, _destination.GetSpan(value.Length));
        _destination.Advance(written);
    }

    /// <summary>Writes a 32-bit count of bytes, then the bytes.</summary>
    public void WriteCountedBytes(ReadOnlySpan<byte> bytes)
    {
        WriteUInt32((uint)bytes.Length);
        bytes.CopyTo(_destination.GetSpan(bytes.Length));
        _destination.Advance(bytes.Length);
    }

    /// <summary>Writes a 32-bit element count, then each element.</summary>
    public void WriteList<T>(IReadOnlyCollection<T> elements, Action<PacketWriter, T> writeElement)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(writeElement);

        WriteUInt32((uint)elements.Count);
        foreach (var element in elements)
        {
            writeElement(this, element);
        }
    }
}
