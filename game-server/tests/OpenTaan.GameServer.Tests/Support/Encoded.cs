using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace OpenTaan.Tests.GameServer;

/// <summary>Builds bodies for tests.</summary>
internal static class Encoded
{
    /// <summary>Runs a codec's <c>Write</c> and hands back what it wrote.</summary>
    public static byte[] Bytes(Action<IBufferWriter<byte>> write)
    {
        var destination = new ArrayBufferWriter<byte>();
        write(destination);
        return destination.WrittenSpan.ToArray();
    }

    /// <summary>A 32-bit value as it appears in a body: little-endian.</summary>
    public static byte[] UInt32(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>A string written wide: a 32-bit count of UTF-16 code units, then the units.</summary>
    public static byte[] Utf16(string value) => [.. UInt32((uint)value.Length), .. Encoding.Unicode.GetBytes(value)];

    /// <summary>A string written narrow: a 32-bit count, then one byte per character.</summary>
    public static byte[] OneBytePerCharacter(string value) => [.. UInt32((uint)value.Length), .. Encoding.Latin1.GetBytes(value)];
}
