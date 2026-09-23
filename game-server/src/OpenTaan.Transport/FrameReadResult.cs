namespace OpenTaan.Transport;

/// <summary>How a read of the front of a receive buffer ended.</summary>
public enum FrameStatus
{
    /// <summary>The buffer does not hold a whole frame yet. Read more and try again.</summary>
    Incomplete,

    /// <summary>A frame was read.</summary>
    Complete,

    /// <summary>The frame cannot be trusted. The connection has no way back in step.</summary>
    Invalid,
}

/// <summary>Why a frame was refused.</summary>
public enum FrameError
{
    /// <summary>The frame was not refused.</summary>
    None,

    /// <summary>
    /// The header declared a length no peer framing against the same receive window
    /// could have sent. Either the seed has desynchronised or the peer is not the
    /// client it claims to be.
    /// </summary>
    DeclaredLengthOutOfRange,

    /// <summary>The trailing checksum did not match the body it covers.</summary>
    ChecksumMismatch,
}

/// <summary>The outcome of reading one frame.</summary>
/// <param name="Status">Whether a frame was read, more data is needed, or the frame was refused.</param>
/// <param name="BytesConsumed">Bytes to drop from the front of the buffer. Zero unless a frame was read.</param>
/// <param name="BodyLength">Bytes written to the destination. Zero unless a frame was read.</param>
/// <param name="Error">Why the frame was refused, when it was.</param>
public readonly record struct FrameReadResult(
    FrameStatus Status,
    int BytesConsumed,
    int BodyLength,
    FrameError Error = FrameError.None);
