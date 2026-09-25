using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Threading.Channels;
using OpenTaan.GameServer.Accounts;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Handshake;
using OpenTaan.Transport;

namespace OpenTaan.GameServer.Sessions;

/// <summary>
/// One client connection, from the handshake to the close.
/// </summary>
/// <remarks>
/// <para>
/// Two tasks run per connection. The read loop reads frames, one at a time and in
/// order, and awaits each packet's handler before reading the next, so a client's
/// requests are answered in the order it sent them. The write loop is the only code
/// that touches the socket's sending side: replies are queued with
/// <see cref="Send"/>, which never blocks, and the write loop frames them with the
/// send seed in queue order. Seeds therefore advance exactly once per frame in each
/// direction, which is the one thing both ends must agree on.
/// </para>
/// <para>
/// A client that stops reading fills its queue or stalls a write past the send
/// timeout; either closes that connection alone. Nothing another connection does can
/// wait on it.
/// </para>
/// <para>
/// Every way a connection ends goes through <see cref="Close"/>, which keeps the first
/// reason and stops both loops. The frames that cannot be trusted, bodies that cannot
/// be read, and requests that cannot be answered all close the connection rather than
/// being skipped: the client never re-sends, so skipping would leave it waiting.
/// </para>
/// </remarks>
public sealed partial class ClientSession
{
    private const int NotClosed = -1;

    private readonly Stream _stream;
    private readonly SessionServices _services;
    private readonly Channel<ReadOnlyMemory<byte>> _outbound;
    private readonly CancellationTokenSource _closing = new();
    private SessionSeeds _seeds = new(OpenTaanCipher.PlaintextSeed);
    private int _closeReason = NotClosed;

    /// <summary>Wraps an accepted connection. Nothing is sent until <see cref="RunAsync"/>.</summary>
    public ClientSession(long id, EndPoint? remoteEndPoint, Stream stream, SessionServices services)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(services);

        Id = id;
        RemoteEndPoint = remoteEndPoint;
        _stream = stream;
        _services = services;
        _outbound = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(services.Options.SendQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>The connection's id, unique for the life of the process.</summary>
    public long Id { get; }

    /// <summary>Where the connection came from.</summary>
    public EndPoint? RemoteEndPoint { get; }

    /// <summary>The accounts this connection logged in with, one per slot. Empty before login.</summary>
    public IReadOnlyList<Account> Accounts { get; private set; } = [];

    /// <summary>Whether this connection has logged in.</summary>
    public bool IsLoggedIn => Accounts.Count > 0;

    private ILogger Logger => _services.Logger;

    /// <summary>
    /// Logs this connection in with <paramref name="accounts"/>, unless one of them is live
    /// on another connection. They stay this connection's until it closes.
    /// </summary>
    /// <returns>Null when logged in; otherwise the connection holding one of the accounts.</returns>
    public ClientSession? TryLogIn(IReadOnlyList<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentOutOfRangeException.ThrowIfZero(accounts.Count, nameof(accounts));

        var holder = _services.LoggedIn.TryClaim(this, accounts);
        if (holder is null)
        {
            Accounts = accounts;
        }

        return holder;
    }

    /// <summary>
    /// Queues <paramref name="packet"/> to be sent. Never blocks: a queue that is full
    /// means the client has stopped reading, and the connection is closed.
    /// </summary>
    public void Send(IPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var body = new ArrayBufferWriter<byte>();
        packet.Write(body);

        if (body.WrittenCount > TransportLimits.MaxEncryptedBody)
        {
            // The client would wait for the rest of a frame that cannot fit its window,
            // without disconnecting and without an error. Refused here, where the
            // packet that did it can still be named.
            LogOversized(Logger, Id, Describe(body.WrittenSpan), body.WrittenCount, TransportLimits.MaxEncryptedBody);
            Close(CloseReason.OversizedReply);
            return;
        }

        if (!_outbound.Writer.TryWrite(body.WrittenMemory))
        {
            Close(CloseReason.SendQueueFull);
        }
    }

    /// <summary>Ends the connection. Only the first reason is kept; later calls do nothing.</summary>
    public void Close(CloseReason reason)
    {
        if (Interlocked.CompareExchange(ref _closeReason, (int)reason, NotClosed) == NotClosed)
        {
            _outbound.Writer.TryComplete();
            _closing.Cancel();
        }
    }

    /// <summary>
    /// Runs the connection until it closes, then closes the stream.
    /// </summary>
    /// <param name="stopping">Closes the connection when the server shuts down.</param>
    /// <returns>Why the connection ended.</returns>
    public async Task<CloseReason> RunAsync(CancellationToken stopping)
    {
        LogOpened(Logger, Id, RemoteEndPoint);

        Task writing = Task.CompletedTask;
        try
        {
            using (stopping.Register(() => Close(CloseReason.ServerStopping)))
            {
                await SendHandshakeAsync().ConfigureAwait(false);
                writing = WriteLoopAsync();
                await ReadLoopAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closed while a read or the handshake was in flight.
        }
        catch (IOException exception)
        {
            LogTransportFailed(Logger, Id, exception.Message);
            Close(CloseReason.TransportError);
        }
        finally
        {
            // Every path above has set a reason by now. Only an exception this method
            // does not catch arrives here without one, and it leaves as a server error.
            Close(CloseReason.ServerError);

            // The read loop awaits every handler, so no login can still be under way here.
            _services.LoggedIn.Release(this, Accounts);

            await writing.ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
            _closing.Dispose();
        }

        var reason = (CloseReason)_closeReason;
        LogClosed(Logger, Id, reason, _seeds.ReceivedFrames, _seeds.SentFrames);
        return reason;
    }

    private async Task SendHandshakeAsync()
    {
        var keys = _services.HandshakeKeys();
        var greeting = new PcFirstAccept(keys.Seed1, keys.Seed2, keys.Shift);

        var body = new ArrayBufferWriter<byte>();
        greeting.Write(body);

        // The only plaintext frame. Neither end has a seed until the client has read it.
        var frame = new byte[FrameWriter.FrameLength(body.WrittenCount, OpenTaanCipher.PlaintextSeed)];
        FrameWriter.Write(body.WrittenSpan, OpenTaanCipher.PlaintextSeed, frame);
        await WriteFrameAsync(frame).ConfigureAwait(false);

        _seeds = new SessionSeeds(keys.BaseSeed);
        LogHandshakeSent(Logger, Id, greeting);
    }

    private async Task ReadLoopAsync()
    {
        // The receive window bounds every frame the client can send, so after the
        // bytes of finished frames are dropped, what is left always fits.
        var buffer = new byte[TransportLimits.ReceiveWindow];
        var body = new byte[TransportLimits.MaxEncryptedBody];
        var filled = 0;

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        idle.CancelAfter(_services.Options.IdleTimeout);

        while (true)
        {
            int received;
            try
            {
                received = await _stream.ReadAsync(buffer.AsMemory(filled), idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!_closing.IsCancellationRequested)
            {
                Close(CloseReason.IdleTimeout);
                return;
            }

            if (received == 0)
            {
                Close(CloseReason.PeerClosed);
                return;
            }

            filled += received;
            var consumed = 0;

            while (true)
            {
                var frame = FrameReader.TryRead(buffer.AsSpan(consumed, filled - consumed), _seeds.Receive, body);

                if (frame.Status == FrameStatus.Incomplete)
                {
                    break;
                }

                if (frame.Status == FrameStatus.Invalid)
                {
                    LogInvalidFrame(Logger, Id, frame.Error, _seeds.ReceivedFrames);
                    Close(CloseReason.InvalidFrame);
                    return;
                }

                _seeds.AdvanceReceive();
                consumed += frame.BytesConsumed;
                idle.CancelAfter(_services.Options.IdleTimeout);

                await DispatchAsync(body.AsMemory(0, frame.BodyLength)).ConfigureAwait(false);

                if (_closing.IsCancellationRequested)
                {
                    return;
                }
            }

            buffer.AsSpan(consumed, filled - consumed).CopyTo(buffer);
            filled -= consumed;
        }
    }

    private async ValueTask DispatchAsync(ReadOnlyMemory<byte> body)
    {
        try
        {
            await _services.Dispatcher.DispatchAsync(this, body.Span, _closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closed while the handler was waiting.
        }
        catch (Exception exception)
        {
            // A handler's failure ends its own connection, never the server.
            LogHandlerFailed(Logger, exception, Id, Describe(body.Span));
            Close(CloseReason.ServerError);
        }
    }

    private async Task WriteLoopAsync()
    {
        var frame = new byte[TransportLimits.ReceiveWindow];

        try
        {
            // Completed by Close, so the loop ends once the queue is drained or abandoned.
            while (await _outbound.Reader.WaitToReadAsync(_closing.Token).ConfigureAwait(false))
            {
                while (_outbound.Reader.TryRead(out var body))
                {
                    var length = FrameWriter.Write(body.Span, _seeds.Send, frame);
                    _seeds.AdvanceSend();
                    await WriteFrameAsync(frame.AsMemory(0, length)).ConfigureAwait(false);
                    LogSent(Logger, Id, Describe(body.Span), body.Length);
                }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closed while a frame was waiting or on its way.
        }
        catch (IOException exception)
        {
            LogTransportFailed(Logger, Id, exception.Message);
            Close(CloseReason.TransportError);
        }
    }

    private async Task WriteFrameAsync(ReadOnlyMemory<byte> frame)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        timeout.CancelAfter(_services.Options.SendTimeout);

        try
        {
            await _stream.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_closing.IsCancellationRequested)
        {
            Close(CloseReason.SendTimeout);
            throw;
        }
    }

    private string Describe(ReadOnlySpan<byte> body) =>
        body.Length < sizeof(uint)
            ? "an empty body"
            : _services.Names.Describe(BinaryPrimitives.ReadUInt32LittleEndian(body));

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} opened from {RemoteEndPoint}")]
    private static partial void LogOpened(ILogger logger, long connectionId, EndPoint? remoteEndPoint);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId} closed: {Reason}, after {Received} frames in and {Sent} out")]
    private static partial void LogClosed(ILogger logger, long connectionId, CloseReason reason, long received, long sent);

    // The greeting prints its shift and never its seeds.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Connection {ConnectionId} sent the handshake: {Greeting}")]
    private static partial void LogHandshakeSent(ILogger logger, long connectionId, PcFirstAccept greeting);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Connection {ConnectionId} sent {Packet}, {Length} bytes")]
    private static partial void LogSent(ILogger logger, long connectionId, string packet, int length);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a frame that cannot be read ({Error}) after {Received} good ones; closing it")]
    private static partial void LogInvalidFrame(ILogger logger, long connectionId, FrameError error, long received);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Connection {ConnectionId} would have been sent {Packet} at {Length} bytes, past the {Limit}-byte limit; closing it")]
    private static partial void LogOversized(ILogger logger, long connectionId, string packet, int length, int limit);

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection {ConnectionId}: the handler for {Packet} failed; closing it")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, long connectionId, string packet);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId}: the socket failed: {Failure}")]
    private static partial void LogTransportFailed(ILogger logger, long connectionId, string failure);
}
