using System.Buffers.Binary;
using OpenTaan.Transport;

namespace OpenTaan.Tests.Transport;

public sealed class FrameReaderTests
{
    private const uint Seed = 0x3092A13F;

    [Fact]
    public void Reads_a_frame_the_writer_produced()
    {
        var body = Body(40);
        var frame = Frame(body, Seed);
        var destination = Destination();

        var result = FrameReader.TryRead(frame, Seed, destination);

        Assert.Equal(FrameStatus.Complete, result.Status);
        Assert.Equal(frame.Length, result.BytesConsumed);
        Assert.Equal(body.Length, result.BodyLength);
        Assert.Equal(body, destination.AsSpan(0, result.BodyLength).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Waits_for_a_header_that_has_not_all_arrived(int available)
    {
        var frame = Frame(Body(8), Seed);

        var result = FrameReader.TryRead(frame.AsSpan(0, available), Seed, Destination());

        Assert.Equal(FrameStatus.Incomplete, result.Status);
        Assert.Equal(0, result.BytesConsumed);
    }

    [Fact]
    public void Waits_for_a_body_that_has_not_all_arrived()
    {
        var frame = Frame(Body(40), Seed);

        var result = FrameReader.TryRead(frame.AsSpan(0, frame.Length - 1), Seed, Destination());

        Assert.Equal(FrameStatus.Incomplete, result.Status);
        Assert.Equal(0, result.BytesConsumed);
    }

    [Fact]
    public void Reads_the_first_of_two_frames_and_leaves_the_second()
    {
        var first = Body(8);
        var second = Body(24);
        var buffer = new byte[FrameWriter.FrameLength(first.Length, Seed) + FrameWriter.FrameLength(second.Length, Seed)];

        var written = FrameWriter.Write(first, Seed, buffer);
        FrameWriter.Write(second, Seed, buffer.AsSpan(written));

        var destination = Destination();
        var result = FrameReader.TryRead(buffer, Seed, destination);

        Assert.Equal(FrameStatus.Complete, result.Status);
        Assert.Equal(written, result.BytesConsumed);
        Assert.Equal(first, destination.AsSpan(0, result.BodyLength).ToArray());

        // What is left is exactly the second frame, starting at a frame boundary.
        var next = FrameReader.TryRead(buffer.AsSpan(result.BytesConsumed), Seed, destination);
        Assert.Equal(FrameStatus.Complete, next.Status);
        Assert.Equal(second, destination.AsSpan(0, next.BodyLength).ToArray());
    }

    [Fact]
    public void Refuses_an_impossible_length_instead_of_waiting_for_bytes_that_will_never_come()
    {
        // The declared length is chosen by the sender. Answering Incomplete to a
        // four-gigabyte length would leave the connection buffering until something
        // outside it gave up, so it is refused on the header alone.
        var header = new byte[TransportLimits.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, HeaderDeclaring(Seed, uint.MaxValue));

        var result = FrameReader.TryRead(header, Seed, Destination());

        Assert.Equal(FrameStatus.Invalid, result.Status);
        Assert.Equal(FrameError.DeclaredLengthOutOfRange, result.Error);
        Assert.Equal(0, result.BytesConsumed);
    }

    [Fact]
    public void Refuses_a_body_one_byte_past_the_receive_window()
    {
        var header = new byte[TransportLimits.HeaderSize];
        var justTooBig = (uint)TransportLimits.MaxEncryptedBody + TransportLimits.ChecksumSize + 1;
        BinaryPrimitives.WriteUInt32LittleEndian(header, HeaderDeclaring(Seed, justTooBig));

        var result = FrameReader.TryRead(header, Seed, Destination());

        Assert.Equal(FrameStatus.Invalid, result.Status);
        Assert.Equal(FrameError.DeclaredLengthOutOfRange, result.Error);
    }

    [Fact]
    public void Refuses_a_length_too_small_to_cover_its_own_checksum()
    {
        var header = new byte[TransportLimits.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, HeaderDeclaring(Seed, 3));

        var result = FrameReader.TryRead(header, Seed, Destination());

        Assert.Equal(FrameStatus.Invalid, result.Status);
        Assert.Equal(FrameError.DeclaredLengthOutOfRange, result.Error);
    }

    [Fact]
    public void Accepts_an_empty_body()
    {
        var frame = Frame([], Seed);

        var result = FrameReader.TryRead(frame, Seed, Destination());

        Assert.Equal(FrameStatus.Complete, result.Status);
        Assert.Equal(0, result.BodyLength);
        Assert.Equal(TransportLimits.EncryptedOverhead, result.BytesConsumed);
    }

    [Fact]
    public void Refuses_a_body_whose_checksum_does_not_cover_it()
    {
        var frame = Frame(Body(24), Seed);
        frame[TransportLimits.HeaderSize] ^= 0xFF;

        var result = FrameReader.TryRead(frame, Seed, Destination());

        Assert.Equal(FrameStatus.Invalid, result.Status);
        Assert.Equal(FrameError.ChecksumMismatch, result.Error);
    }

    [Fact]
    public void Reads_a_damaged_body_when_the_checksum_is_not_checked()
    {
        var frame = Frame(Body(24), Seed);
        frame[TransportLimits.HeaderSize] ^= 0xFF;

        var result = FrameReader.TryRead(frame, Seed, Destination(), verifyChecksum: false);

        Assert.Equal(FrameStatus.Complete, result.Status);
    }

    [Fact]
    public void Reads_a_plaintext_frame_whose_header_is_its_length()
    {
        var body = Body(12);
        var frame = Frame(body, OpenTaanCipher.PlaintextSeed);
        var destination = Destination();

        Assert.Equal((uint)body.Length, BinaryPrimitives.ReadUInt32LittleEndian(frame));

        var result = FrameReader.TryRead(frame, OpenTaanCipher.PlaintextSeed, destination);

        Assert.Equal(FrameStatus.Complete, result.Status);
        Assert.Equal(body, destination.AsSpan(0, result.BodyLength).ToArray());
    }

    [Fact]
    public void Refuses_a_plaintext_length_past_the_receive_window()
    {
        var header = new byte[TransportLimits.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)TransportLimits.MaxPlaintextBody + 1);

        var result = FrameReader.TryRead(header, OpenTaanCipher.PlaintextSeed, Destination());

        Assert.Equal(FrameStatus.Invalid, result.Status);
        Assert.Equal(FrameError.DeclaredLengthOutOfRange, result.Error);
    }

    [Fact]
    public void Insists_on_somewhere_to_put_the_largest_legal_body()
    {
        var tooSmall = new byte[TransportLimits.MaxEncryptedBody - 1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            FrameReader.TryRead(Frame(Body(4), Seed), Seed, tooSmall);
        });
    }

    /// <summary>
    /// A header that decodes to <paramref name="declared"/>, built without reaching
    /// for the mask: encoding a zero-length body gives seed ^ 4 ^ mask, so undoing
    /// the 4 and applying the wanted value leaves seed ^ mask ^ declared.
    /// </summary>
    private static uint HeaderDeclaring(uint seed, uint declared) =>
        OpenTaanCipher.EncodeHeader(seed, 0) ^ (uint)TransportLimits.ChecksumSize ^ declared;

    private static byte[] Destination() => new byte[TransportLimits.MaxEncryptedBody];

    private static byte[] Frame(byte[] body, uint seed)
    {
        var frame = new byte[FrameWriter.FrameLength(body.Length, seed)];
        FrameWriter.Write(body, seed, frame);
        return frame;
    }

    private static byte[] Body(int length)
    {
        var body = new byte[length];
        for (var i = 0; i < length; i++)
        {
            body[i] = (byte)((i * 11) + 5);
        }

        return body;
    }
}
