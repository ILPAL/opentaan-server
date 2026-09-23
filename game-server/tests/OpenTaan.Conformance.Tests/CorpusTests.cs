using OpenTaan.Transport;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// Checks on the recorded sessions themselves: that they are complete, and that they
/// agree with the declared layouts. A damaged or partly copied corpus should fail
/// here rather than surface later as a protocol bug.
/// </summary>
public sealed class CorpusTests
{
    [Fact]
    public void ThereIsSomethingToCheck()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        Assert.NotEmpty(WireContract.Sessions);
        Assert.All(WireContract.Sessions, session => Assert.NotEmpty(session.Exchanges));
    }

    [Fact]
    public void EverySessionNamesWhatItRecords()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        Assert.All(WireContract.Sessions, session =>
        {
            Assert.False(string.IsNullOrWhiteSpace(session.Lifecycle), session.Origin);
            Assert.False(string.IsNullOrWhiteSpace(session.Clock), session.Origin);
        });
    }

    [Fact]
    public void NoTwoSessionsRecordTheSameLifecycle()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var duplicated = WireContract.Sessions
            .GroupBy(session => session.Lifecycle)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void EveryRecordedFrameLeadsWithAKnownPacket()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var known = WireContract.Spec.Classes.Select(c => c.RegistryId).ToHashSet();

        var unknown = Frames()
            .Where(frame => !known.Contains(LeadingId(frame.Frame)))
            .Select(frame => $"{frame.Origin}: id {LeadingId(frame.Frame)}")
            .ToArray();

        Assert.Empty(unknown);
    }

    [Fact]
    public void AFrameIsNeverShorterThanTheIdThatNamesIt()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var stunted = Frames()
            .Where(frame => frame.Frame.Body().Length < sizeof(uint))
            .Select(frame => frame.Origin)
            .ToArray();

        Assert.Empty(stunted);
    }

    [Fact]
    public void AFrameOfAFixedSizePacketIsExactlyThatSize()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var byId = WireContract.Spec.Classes.ToDictionary(c => c.RegistryId);

        var wrong = Frames()
            .Select(frame => (frame.Origin, Body: frame.Frame.Body()))
            .Where(frame => byId.TryGetValue(LeadingIdOf(frame.Body), out var declared)
                            && declared.FixedSize is { } size
                            && size != frame.Body.Length)
            .Select(frame => $"{frame.Origin}: {byId[LeadingIdOf(frame.Body)].WireName} " +
                             $"declares {byId[LeadingIdOf(frame.Body)].FixedSize} but the frame is {frame.Body.Length}")
            .ToArray();

        Assert.Empty(wrong);
    }

    [Fact]
    public void APacketTheLayoutsDescribeIsNeverLeftEmpty()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        // A class the contract describes must list its own id as the first field;
        // one it does not describe lists nothing at all.
        var malformed = WireContract.Spec.Classes
            .Where(c => c.Fields.Count > 0 && c.Fields[0].Name != "registry_id")
            .Select(c => c.WireName)
            .ToArray();

        Assert.Empty(malformed);
    }

    [Fact]
    public void EverySessionHandsOverWorkableHandshakeValues()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        Assert.All(WireContract.Sessions, session =>
        {
            var handshake = session.Handshake;
            Assert.NotNull(handshake);

            // Zero would tell the client the connection is unencrypted, so a session
            // that derived to it could never have been recorded.
            var seed = OpenTaanCipher.DeriveSeed(handshake.FirstSeed, handshake.SecondSeed, handshake.Shift);
            Assert.NotEqual(OpenTaanCipher.PlaintextSeed, seed);
        });
    }

    internal static IEnumerable<(string Origin, OutboundFrame Frame)> Frames() =>
        from session in WireContract.Sessions
        from exchange in session.Exchanges
        from frame in exchange.Frames
        select (session.Origin, frame);

    private static uint LeadingId(OutboundFrame frame) => LeadingIdOf(frame.Body());

    private static uint LeadingIdOf(byte[] body) =>
        body.Length >= sizeof(uint)
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body)
            : 0;
}
