using System.Buffers;
using System.Buffers.Binary;

namespace OpenTaan.Tests.Protocol;

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

    /// <summary>A 16-bit value as it appears in a body: little-endian.</summary>
    public static byte[] UInt16(ushort value)
    {
        var bytes = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>A 32-bit value as it appears in a body: little-endian.</summary>
    public static byte[] UInt32(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>A string as it appears in a body: a 32-bit count of UTF-16 code units, then the units.</summary>
    public static byte[] Utf16(string value) => [.. UInt32((uint)value.Length), .. System.Text.Encoding.Unicode.GetBytes(value)];
}
