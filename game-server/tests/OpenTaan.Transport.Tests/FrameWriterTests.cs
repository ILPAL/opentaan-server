using OpenTaan.Transport;

namespace OpenTaan.Tests.Transport;

public sealed class FrameWriterTests
{
    private const uint Seed = 0x3092A13F;

    [Fact]
    public void An_encrypted_frame_costs_its_body_plus_a_header_and_a_trailer()
    {
        Assert.Equal(40 + TransportLimits.EncryptedOverhead, FrameWriter.FrameLength(40, Seed));
    }

    [Fact]
    public void A_plaintext_frame_costs_its_body_plus_a_header()
    {
        Assert.Equal(40 + TransportLimits.PlaintextOverhead, FrameWriter.FrameLength(40, OpenTaanCipher.PlaintextSeed));
    }

    [Fact]
    public void Frames_the_largest_body_the_receive_window_allows()
    {
        var body = new byte[TransportLimits.MaxEncryptedBody];
        var frame = new byte[FrameWriter.FrameLength(body.Length, Seed)];

        var written = FrameWriter.Write(body, Seed, frame);

        Assert.Equal(TransportLimits.ReceiveWindow, written);
    }

    [Fact]
    public void Refuses_a_body_one_byte_past_what_the_client_could_receive()
    {
        // The client would not report this frame. It would wait for one that cannot
        // fit its window, in silence, so the refusal has to happen on this side.
        var body = new byte[TransportLimits.MaxEncryptedBody + 1];
        var frame = new byte[TransportLimits.ReceiveWindow + TransportLimits.EncryptedOverhead];

        Assert.Throws<ArgumentOutOfRangeException>(() => FrameWriter.Write(body, Seed, frame));
    }

    [Fact]
    public void Refuses_an_oversized_plaintext_body()
    {
        var body = new byte[TransportLimits.MaxPlaintextBody + 1];
        var frame = new byte[TransportLimits.ReceiveWindow + TransportLimits.EncryptedOverhead];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FrameWriter.Write(body, OpenTaanCipher.PlaintextSeed, frame));
    }

    [Fact]
    public void Refuses_a_destination_that_cannot_hold_the_frame()
    {
        var body = new byte[16];
        var tooSmall = new byte[FrameWriter.FrameLength(body.Length, Seed) - 1];

        Assert.Throws<ArgumentOutOfRangeException>(() => FrameWriter.Write(body, Seed, tooSmall));
    }

    [Fact]
    public void Leaves_the_body_unreadable_on_the_wire()
    {
        var body = new byte[32];
        Array.Fill(body, (byte)0xAB);
        var frame = new byte[FrameWriter.FrameLength(body.Length, Seed)];

        FrameWriter.Write(body, Seed, frame);

        var onWire = frame.AsSpan(TransportLimits.HeaderSize, body.Length).ToArray();
        Assert.NotEqual(body, onWire);
    }
}
