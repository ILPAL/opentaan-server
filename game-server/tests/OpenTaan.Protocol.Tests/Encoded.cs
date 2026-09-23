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

    /// <summary>A 32-bit value as it appears in a body: little-endian.</summary>
    public static byte[] UInt32(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
