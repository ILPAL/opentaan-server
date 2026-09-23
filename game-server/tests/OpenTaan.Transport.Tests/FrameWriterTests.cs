using OpenTaan.Transport;

namespace OpenTaan.Tests.Transport;

public sealed class FrameWriterTests
{
    private const uint Seed = 0x3092A13F;

    [Fact]
    public void AnEncryptedFrameCostsItsBodyPlusAHeaderAndATrailer()
    {
        Assert.Equal(40 + TransportLimits.EncryptedOverhead, FrameWriter.FrameLength(40, Seed));
    }

    [Fact]
    public void APlaintextFrameCostsItsBodyPlusAHeader()
    {
        Assert.Equal(40 + TransportLimits.PlaintextOverhead, FrameWriter.FrameLength(40, OpenTaanCipher.PlaintextSeed));
    }

    [Fact]
    public void FramesTheLargestBodyTheReceiveWindowAllows()
    {
        var body = new byte[TransportLimits.MaxEncryptedBody];
        var frame = new byte[FrameWriter.FrameLength(body.Length, Seed)];

        var written = FrameWriter.Write(body, Seed, frame);

        Assert.Equal(TransportLimits.ReceiveWindow, written);
    }

    [Fact]
    public void RefusesABodyOneBytePastWhatTheClientCouldReceive()
    {
        // The client would not report this frame. It would wait for one that cannot
        // fit its window, in silence, so the refusal has to happen on this side.
        var body = new byte[TransportLimits.MaxEncryptedBody + 1];
        var frame = new byte[TransportLimits.ReceiveWindow + TransportLimits.EncryptedOverhead];

        Assert.Throws<ArgumentOutOfRangeException>(() => FrameWriter.Write(body, Seed, frame));
    }

    [Fact]
    public void RefusesAnOversizedPlaintextBody()
    {
        var body = new byte[TransportLimits.MaxPlaintextBody + 1];
        var frame = new byte[TransportLimits.ReceiveWindow + TransportLimits.EncryptedOverhead];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FrameWriter.Write(body, OpenTaanCipher.PlaintextSeed, frame));
    }

    [Fact]
    public void RefusesADestinationThatCannotHoldTheFrame()
    {
        var body = new byte[16];
        var tooSmall = new byte[FrameWriter.FrameLength(body.Length, Seed) - 1];

        Assert.Throws<ArgumentOutOfRangeException>(() => FrameWriter.Write(body, Seed, tooSmall));
    }

    [Fact]
    public void LeavesTheBodyUnreadableOnTheWire()
    {
        var body = new byte[32];
        Array.Fill(body, (byte)0xAB);
        var frame = new byte[FrameWriter.FrameLength(body.Length, Seed)];

        FrameWriter.Write(body, Seed, frame);

        var onWire = frame.AsSpan(TransportLimits.HeaderSize, body.Length).ToArray();
        Assert.NotEqual(body, onWire);
    }
}
