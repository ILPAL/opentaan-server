using System.Buffers;
using System.Net.Sockets;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Handshake;
using OpenTaan.Transport;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// The client's end of a connection, built from the same transport and codecs the
/// server uses: it reads the handshake, then frames and reads with its own seeds.
/// </summary>
internal sealed class LoopbackClient : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly NetworkStream _stream;
    private readonly byte[] _buffer = new byte[TransportLimits.ReceiveWindow];
    private readonly byte[] _body = new byte[TransportLimits.MaxEncryptedBody];
    private int _filled;
    private SessionSeeds _seeds = new(OpenTaanCipher.PlaintextSeed);

    private LoopbackClient(Socket socket)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>The body of the handshake frame, exactly as it arrived.</summary>
    public byte[] GreetingBody { get; private set; } = [];

    /// <summary>The handshake, decoded.</summary>
    public PcFirstAccept Greeting { get; private set; } = null!;

    /// <summary>Reads the handshake on a connected socket and takes up its seeds.</summary>
    public static async Task<LoopbackClient> AttachAsync(Socket socket)
    {
        var client = new LoopbackClient(socket);

        client.GreetingBody = await client.ReceiveBodyAsync().ConfigureAwait(false);
        var greeting = PcFirstAccept.TryRead(client.GreetingBody);
        Assert.True(greeting.Succeeded, $"the first frame is not a handshake: {greeting.Error}");

        client.Greeting = greeting.Packet;
        client._seeds = new SessionSeeds(new HandshakeKeys(greeting.Packet.Seed1, greeting.Packet.Seed2, greeting.Packet.Shift).BaseSeed);
        return client;
    }

    /// <summary>Sends a packet in the next frame.</summary>
    public Task SendAsync(IPacket packet)
    {
        var body = new ArrayBufferWriter<byte>();
        packet.Write(body);
        return SendBodyAsync(body.WrittenMemory);
    }

    /// <summary>Sends any body in the next frame.</summary>
    public async Task SendBodyAsync(ReadOnlyMemory<byte> body)
    {
        await SendRawAsync(Frame(body.Span)).ConfigureAwait(false);
    }

    /// <summary>Frames a body with the next send seed, and moves the seed on.</summary>
    public byte[] Frame(ReadOnlySpan<byte> body)
    {
        var frame = new byte[FrameWriter.FrameLength(body.Length, _seeds.Send)];
        FrameWriter.Write(body, _seeds.Send, frame);
        _seeds.AdvanceSend();
        return frame;
    }

    /// <summary>The seed the next frame will be sent with.</summary>
    public uint NextSendSeed => _seeds.Send;

    /// <summary>Sends bytes as they are, framed or not.</summary>
    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes)
    {
        await _stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>Reads the next packet and decodes it, failing the test if it is not one.</summary>
    public async Task<TPacket> ReceiveAsync<TPacket>(PacketDecoder<TPacket> decode)
        where TPacket : class
    {
        var body = await ReceiveBodyAsync().ConfigureAwait(false);
        var read = decode(body);
        Assert.True(read.Succeeded, $"expected {typeof(TPacket).Name}, which refused the next body: {read.Error}");
        return read.Packet;
    }

    /// <summary>Reads the next frame's body.</summary>
    public async Task<byte[]> ReceiveBodyAsync()
    {
        using var patience = new CancellationTokenSource(Patience);

        while (true)
        {
            var frame = FrameReader.TryRead(_buffer.AsSpan(0, _filled), _seeds.Receive, _body);
            if (frame.Status == FrameStatus.Complete)
            {
                if (_seeds.Receive != OpenTaanCipher.PlaintextSeed)
                {
                    _seeds.AdvanceReceive();
                }

                _buffer.AsSpan(frame.BytesConsumed, _filled - frame.BytesConsumed).CopyTo(_buffer);
                _filled -= frame.BytesConsumed;
                return _body.AsSpan(0, frame.BodyLength).ToArray();
            }

            Assert.Equal(FrameStatus.Incomplete, frame.Status);

            var received = await _stream.ReadAsync(_buffer.AsMemory(_filled), patience.Token).ConfigureAwait(false);
            Assert.True(received > 0, "the server closed the connection instead of answering");
            _filled += received;
        }
    }

    /// <summary>Whether the server closes the connection, sending nothing more, within the patience allowed.</summary>
    public async Task<bool> IsClosedByServerAsync()
    {
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            var received = await _stream.ReadAsync(_buffer.AsMemory(_filled), patience.Token).ConfigureAwait(false);
            return received == 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
