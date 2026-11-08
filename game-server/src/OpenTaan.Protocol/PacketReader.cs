using System.Buffers.Binary;
using System.Text;

namespace OpenTaan.Protocol;

/// <summary>Reads one element of a list. See <see cref="PacketReader.ReadList{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <param name="reader">The reader, positioned at the element.</param>
public delegate T ElementReader<out T>(ref PacketReader reader);

/// <summary>
/// Reads the fields of a packet body, front to back, little-endian.
/// </summary>
/// <remarks>
/// The first failure is kept and every later read returns a default value without
/// moving, so a codec reads its fields in order and asks once, in
/// <see cref="Finish{TPacket}"/>, whether all of them were there. Nothing here trusts
/// a length from the body: a count is checked against the bytes actually left
/// before anything is allocated for it, so a forged count costs the sender a refusal
/// rather than costing the server memory.
/// </remarks>
public ref struct PacketReader
{
    // Strict, so that a body with a broken surrogate pair is refused rather than
    // silently rewritten, which would also make it re-encode to different bytes.
    private static readonly UnicodeEncoding Utf16 = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    private readonly ReadOnlySpan<byte> _body;
    private int _position;

    /// <summary>Starts reading at the front of <paramref name="body"/>.</summary>
    public PacketReader(ReadOnlySpan<byte> body)
    {
        _body = body;
    }

    /// <summary>The first failure, or <see cref="PacketError.None"/>.</summary>
    public PacketError Error { get; private set; }

    /// <summary>Bytes not read yet.</summary>
    public readonly int Remaining => _body.Length - _position;

    /// <summary>Reads a registry id and refuses the body unless it is <paramref name="expected"/>.</summary>
    public void ExpectRegistryId(uint expected)
    {
        var actual = ReadUInt32();
        if (Error == PacketError.None && actual != expected)
        {
            Fail(PacketError.WrongRegistryId);
        }
    }

    /// <summary>Reads an unsigned 8-bit value.</summary>
    public byte ReadByte() => TryTake(sizeof(byte), out var bytes) ? bytes[0] : default;

    /// <summary>Reads an unsigned 16-bit value.</summary>
    public ushort ReadUInt16() =>
        TryTake(sizeof(ushort), out var bytes) ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : default;

    /// <summary>Reads an unsigned 32-bit value.</summary>
    public uint ReadUInt32() =>
        TryTake(sizeof(uint), out var bytes) ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : default;

    /// <summary>Reads a signed 32-bit value, in two's complement.</summary>
    public int ReadInt32() =>
        TryTake(sizeof(int), out var bytes) ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : default;

    /// <summary>Reads an unsigned 64-bit value.</summary>
    public ulong ReadUInt64() =>
        TryTake(sizeof(ulong), out var bytes) ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : default;

    /// <summary>Reads a one-byte flag, refusing anything but 0 and 1.</summary>
    /// <remarks>
    /// Any other value would be read as true and written back as 1, so the packet
    /// would not re-encode to the bytes it came from.
    /// </remarks>
    public bool ReadBoolean()
    {
        var value = ReadByte();
        if (value > 1)
        {
            Fail(PacketError.UnexpectedValue);
        }

        return value == 1;
    }

    /// <summary>Reads a string of UTF-16LE code units, preceded by a 32-bit count of them.</summary>
    public string ReadUtf16String()
    {
        if (!TryTakeCounted(sizeof(char), out var bytes))
        {
            return string.Empty;
        }

        try
        {
            return Utf16.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Fail(PacketError.InvalidText);
            return string.Empty;
        }
    }

    /// <summary>Reads a string of single bytes, preceded by a 32-bit count of them.</summary>
    /// <remarks>
    /// Each byte becomes the character with the same value (Latin-1), which maps all
    /// 256 byte values and back again, so any body re-encodes to the bytes it came from.
    /// </remarks>
    public string ReadByteString() =>
        TryTakeCounted(sizeof(byte), out var bytes) ? Encoding.Latin1.GetString(bytes) : string.Empty;

    /// <summary>Reads raw bytes, preceded by a 32-bit count of them.</summary>
    /// <remarks>The bytes are a view of the body, valid as long as the body is.</remarks>
    public ReadOnlySpan<byte> ReadCountedBytes() =>
        TryTakeCounted(sizeof(byte), out var bytes) ? bytes : default;

    /// <summary>
    /// Reads a list preceded by a 32-bit element count.
    /// </summary>
    /// <param name="minimumElementSize">
    /// The fewest bytes one element can occupy. The count is refused if that many
    /// elements could not fit in the rest of the body.
    /// </param>
    /// <param name="readElement">Reads one element.</param>
    public T[] ReadList<T>(int minimumElementSize, ElementReader<T> readElement)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumElementSize);
        ArgumentNullException.ThrowIfNull(readElement);

        var count = ReadUInt32();
        if (Error != PacketError.None)
        {
            return [];
        }

        if ((ulong)count * (ulong)minimumElementSize > (ulong)Remaining)
        {
            Fail(PacketError.CountOutOfRange);
            return [];
        }

        var elements = new T[count];
        for (var index = 0; index < elements.Length && Error == PacketError.None; index++)
        {
            elements[index] = readElement(ref this);
        }

        return Error == PacketError.None ? elements : [];
    }

    /// <summary>Refuses the body. Only the first failure is kept.</summary>
    public void Fail(PacketError error)
    {
        if (Error == PacketError.None)
        {
            Error = error;
        }
    }

    /// <summary>
    /// Ends the read: <paramref name="packet"/> if every field was there and nothing
    /// is left over, otherwise the first failure.
    /// </summary>
    public readonly PacketReadResult<TPacket> Finish<TPacket>(TPacket packet)
        where TPacket : class
    {
        if (Error != PacketError.None)
        {
            return new PacketReadResult<TPacket>(null, Error);
        }

        return Remaining == 0
            ? new PacketReadResult<TPacket>(packet, PacketError.None)
            : new PacketReadResult<TPacket>(null, PacketError.TrailingBytes);
    }

    private bool TryTakeCounted(int unitSize, out ReadOnlySpan<byte> bytes)
    {
        var count = ReadUInt32();
        bytes = default;
        if (Error != PacketError.None)
        {
            return false;
        }

        var length = (ulong)count * (ulong)unitSize;
        if (length > (ulong)Remaining)
        {
            Fail(PacketError.CountOutOfRange);
            return false;
        }

        return TryTake((int)length, out bytes);
    }

    private bool TryTake(int length, out ReadOnlySpan<byte> bytes)
    {
        if (Error != PacketError.None || length > Remaining)
        {
            Fail(PacketError.Truncated);
            bytes = default;
            return false;
        }

        bytes = _body.Slice(_position, length);
        _position += length;
        return true;
    }
}
