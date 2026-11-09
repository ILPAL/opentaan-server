using System.Buffers;
using OpenTaan.Protocol;

namespace OpenTaan.Tests.Protocol;

public sealed class PacketWriterTests
{
    [Fact]
    public void WritesLittleEndianValues()
    {
        var bytes = Written(writer =>
        {
            writer.WriteByte(0x01);
            writer.WriteUInt16(0x0302);
            writer.WriteUInt32(0x07060504);
            writer.WriteUInt64(0x0F0E0D0C0B0A0908);
        });

        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F], bytes);
    }

    [Fact]
    public void WritesA32BitFloat()
    {
        Assert.Equal([0x00, 0x00, 0xC0, 0x3F], Written(writer => writer.WriteSingle(1.5f)));
    }

    [Fact]
    public void WritesASigned32BitValueInTwosComplement()
    {
        var bytes = Written(writer =>
        {
            writer.WriteInt32(-1000);
            writer.WriteInt32(0x01020304);
        });

        Assert.Equal([0x18, 0xFC, 0xFF, 0xFF, 0x04, 0x03, 0x02, 0x01], bytes);
    }

    [Fact]
    public void CountsAUtf16StringInCodeUnitsAndWritesEachLowByteFirst()
    {
        // "é" is one code unit, U+00E9; "𝄞" is two, a surrogate pair.
        var bytes = Written(writer => writer.WriteUtf16String("é𝄞"));

        Assert.Equal([0x03, 0x00, 0x00, 0x00, 0xE9, 0x00, 0x34, 0xD8, 0x1E, 0xDD], bytes);
    }

    [Fact]
    public void CarriesEveryByteValueThroughASingleByteString()
    {
        var everyByte = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        byte[] body = [.. Encoded.UInt32(256), .. everyByte];

        var reader = new PacketReader(body);
        var text = reader.ReadByteString();

        Assert.Equal(PacketError.None, reader.Error);
        Assert.Equal(body, Written(writer => writer.WriteByteString(text)));
    }

    [Fact]
    public void RefusesACharacterASingleByteStringCannotHold()
    {
        // Writing '?' in its place would send the client a different string.
        Assert.Throws<ArgumentException>(() => Written(writer => writer.WriteByteString("Ā")));
    }

    [Fact]
    public void RefusesABrokenSurrogatePair()
    {
        Assert.Throws<System.Text.EncoderFallbackException>(() => Written(writer => writer.WriteUtf16String("\uD834")));
    }

    [Fact]
    public void WritesAFlagAsZeroOrOne()
    {
        Assert.Equal([0x01, 0x00], Written(writer =>
        {
            writer.WriteBoolean(true);
            writer.WriteBoolean(false);
        }));
    }

    [Fact]
    public void AppendsToWhatIsAlreadyThere()
    {
        var destination = new ArrayBufferWriter<byte>();
        new PacketWriter(destination).WriteByte(0xAA);
        new PacketWriter(destination).WriteByte(0xBB);

        Assert.Equal([0xAA, 0xBB], destination.WrittenSpan.ToArray());
    }

    private static byte[] Written(Action<PacketWriter> write) =>
        Encoded.Bytes(destination => write(new PacketWriter(destination)));
}
