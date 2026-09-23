namespace OpenTaan.Transport;

/// <summary>
/// A connection's seed state. Both directions start at the handshake-derived base
/// seed and advance independently, one step per frame that direction carries.
/// </summary>
/// <remarks>
/// The client keeps the same two counters and advances them at the same points, so
/// the two ends stay in step only while this server advances exactly once per frame.
/// Because an advance onto zero is corrected to one, a carried seed and one
/// recomputed as <c>base + frames * increment</c> agree until the first wrap and
/// never again afterwards. Getting this wrong fails silently: the client decodes a
/// meaningless length, fails its own size check, and stops reading without
/// disconnecting or raising an error.
/// </remarks>
public sealed class SessionSeeds
{
    private readonly uint _baseSeed;

    /// <summary>Starts both directions at <paramref name="baseSeed"/>.</summary>
    public SessionSeeds(uint baseSeed)
    {
        _baseSeed = baseSeed;
        Reset();
    }

    /// <summary>The seed the next inbound frame is decrypted with.</summary>
    public uint Receive { get; private set; }

    /// <summary>The seed the next outbound frame is encrypted with.</summary>
    public uint Send { get; private set; }

    /// <summary>Frames received on this connection since the last reset.</summary>
    public long ReceivedFrames { get; private set; }

    /// <summary>Frames sent on this connection since the last reset.</summary>
    public long SentFrames { get; private set; }

    /// <summary>Returns both directions to the base seed, as a reconnection does.</summary>
    public void Reset()
    {
        Receive = _baseSeed;
        Send = _baseSeed;
        ReceivedFrames = 0;
        SentFrames = 0;
    }

    /// <summary>Advances the receive direction after a frame has been decrypted.</summary>
    public void AdvanceReceive()
    {
        Receive = OpenTaanCipher.AdvanceSeed(Receive);
        ReceivedFrames++;
    }

    /// <summary>Advances the send direction after a frame has been encrypted.</summary>
    public void AdvanceSend()
    {
        Send = OpenTaanCipher.AdvanceSeed(Send);
        SentFrames++;
    }
}
