namespace OpenTaan.Transport;

/// <summary>
/// Frame size bounds. These are measured from the client rather than chosen here.
/// </summary>
/// <remarks>
/// The client reads every connection into one buffer of <see cref="ReceiveWindow"/>
/// bytes and has no maximum-packet check of its own. It only processes a frame once
/// the whole frame is buffered, and the buffered count can never exceed the window,
/// so a frame larger than the window is never satisfied: the client waits for it
/// forever, without disconnecting and without reporting an error. Refusing an
/// oversized frame here is therefore both a memory bound on this server and the only
/// way the other end learns that something is wrong.
/// </remarks>
public static class TransportLimits
{
    /// <summary>Bytes in the frame header.</summary>
    public const int HeaderSize = 4;

    /// <summary>Bytes in the checksum that trails an encrypted body.</summary>
    public const int ChecksumSize = 4;

    /// <summary>The client's per-connection receive buffer, in bytes.</summary>
    public const int ReceiveWindow = 0x10000;

    /// <summary>Framing bytes around an encrypted body: a header and a trailing checksum.</summary>
    public const int EncryptedOverhead = HeaderSize + ChecksumSize;

    /// <summary>Framing bytes around a plaintext body: a header only.</summary>
    public const int PlaintextOverhead = HeaderSize;

    /// <summary>Largest encrypted body that still fits the receive window with its framing.</summary>
    public const int MaxEncryptedBody = ReceiveWindow - EncryptedOverhead;

    /// <summary>Largest plaintext body that still fits the receive window with its framing.</summary>
    public const int MaxPlaintextBody = ReceiveWindow - PlaintextOverhead;

    /// <summary>Largest body this seed's framing allows.</summary>
    public static int MaxBody(uint seed) =>
        seed == OpenTaanCipher.PlaintextSeed ? MaxPlaintextBody : MaxEncryptedBody;
}
