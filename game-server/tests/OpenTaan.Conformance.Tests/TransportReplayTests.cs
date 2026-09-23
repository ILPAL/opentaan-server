using OpenTaan.Transport;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// Drives every recorded frame through the real transport, in the order and with the
/// seeds the session used.
/// </summary>
/// <remarks>
/// The sessions record plaintext packet bodies, so this checks that the transport
/// carries real traffic without altering it: real lengths, real block boundaries, and
/// a seed schedule that runs the whole length of a session rather than a few frames.
/// It is not an independent oracle for the cipher -- the same code frames and reads --
/// but it is the check that fails if a body the client actually sends does not survive
/// the round trip. The independent checks live in <see cref="RegistryIdTests"/> and in
/// the transport's own byte-level tests.
/// </remarks>
public sealed class TransportReplayTests
{
    [Fact]
    public void Every_recorded_frame_survives_the_transport_unchanged()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var destination = new byte[TransportLimits.MaxEncryptedBody];
        var buffer = new byte[TransportLimits.ReceiveWindow];
        var replayed = 0;

        foreach (var session in WireContract.Sessions)
        {
            var seeds = Schedule(session);

            foreach (var (origin, frame) in FramesOf(session))
            {
                var body = frame.Body();

                // The handshake goes out before either end has a seed, so it travels
                // under the unencrypted framing and does not move the schedule on.
                var seed = frame.Encrypted ? seeds.Send : OpenTaanCipher.PlaintextSeed;

                var written = FrameWriter.Write(body, seed, buffer);
                var read = FrameReader.TryRead(buffer.AsSpan(0, written), seed, destination);

                Assert.Equal(FrameStatus.Complete, read.Status);
                Assert.Equal(written, read.BytesConsumed);
                Assert.Equal(body, destination.AsSpan(0, read.BodyLength).ToArray());

                if (frame.Encrypted)
                {
                    seeds.AdvanceSend();
                }

                replayed++;
                Assert.True(read.BodyLength == body.Length, origin);
            }
        }

        Assert.True(replayed > 0, "the corpus was present but recorded no frames");
    }

    [Fact]
    public void No_recorded_frame_is_too_large_for_the_client_to_receive()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var oversized = CorpusTests.Frames()
            .Select(frame => (frame.Origin, Length: frame.Frame.Body().Length, frame.Frame.Encrypted))
            .Where(frame => frame.Length > (frame.Encrypted
                ? TransportLimits.MaxEncryptedBody
                : TransportLimits.MaxPlaintextBody))
            .Select(frame => $"{frame.Origin}: {frame.Length} bytes")
            .ToArray();

        Assert.Empty(oversized);
    }

    [Fact]
    public void A_sessions_seed_schedule_never_reaches_the_value_reserved_for_plaintext()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        foreach (var session in WireContract.Sessions)
        {
            var seeds = Schedule(session);

            foreach (var (_, frame) in FramesOf(session).Where(frame => frame.Frame.Encrypted))
            {
                Assert.NotEqual(OpenTaanCipher.PlaintextSeed, seeds.Send);
                seeds.AdvanceSend();
            }
        }
    }

    private static SessionSeeds Schedule(Session session)
    {
        var handshake = session.Handshake
            ?? throw new InvalidOperationException($"{session.Origin} records no handshake");

        return new SessionSeeds(
            OpenTaanCipher.DeriveSeed(handshake.FirstSeed, handshake.SecondSeed, handshake.Shift));
    }

    private static IEnumerable<(string Origin, OutboundFrame Frame)> FramesOf(Session session) =>
        from exchange in session.Exchanges
        from frame in exchange.Frames
        select (session.Origin, frame);
}
