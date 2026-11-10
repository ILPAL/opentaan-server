namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A stream whose writes stop completing once paused, as a socket's do when the client
/// stops reading and every buffer on the way has filled.
/// </summary>
internal sealed class PausableStream(Stream inner) : Stream
{
    private volatile bool _paused;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Every write from now on waits until it is cancelled.</summary>
    public void Pause() => _paused = true;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_paused)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override ValueTask DisposeAsync() => inner.DisposeAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
