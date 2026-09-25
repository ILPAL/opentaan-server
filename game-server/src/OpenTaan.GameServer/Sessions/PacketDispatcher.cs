using System.Buffers.Binary;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Characters;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Connection;

namespace OpenTaan.GameServer.Sessions;

/// <summary>Reads a packet body. Every codec's <c>TryRead</c> has this shape.</summary>
public delegate PacketReadResult<TPacket> PacketDecoder<TPacket>(ReadOnlySpan<byte> body)
    where TPacket : class;

/// <summary>Acts on a packet that arrived on <paramref name="session"/>.</summary>
public delegate ValueTask PacketHandler<in TPacket>(ClientSession session, TPacket packet, CancellationToken cancellationToken);

/// <summary>Answers a packet that is recognised by its id and not decoded.</summary>
public delegate ValueTask UndecodedHandler(ClientSession session, CancellationToken cancellationToken);

/// <summary>
/// Sends each packet a connection receives to its handler, by the registry id that
/// leads its body.
/// </summary>
/// <remarks>
/// The table is the whole policy, and it is written out rather than discovered, so
/// what the server does with a packet is a decision someone made. Three outcomes:
/// <list type="bullet">
///   <item>a packet with a handler is decoded and handled; a body its codec refuses
///   closes the connection, because a request that cannot be read cannot be answered;</item>
///   <item>a request whose layout is not established, and whose contents are not
///   needed, is answered without being decoded;</item>
///   <item>a packet the client sends without expecting an answer, and that this
///   server has no use for yet, is recognised and dropped;</item>
///   <item>anything else closes the connection. The client never re-sends a request,
///   so one left unanswered stays lost, and some screens stay locked until their
///   reply arrives. A closed connection is at least something the player sees.</item>
/// </list>
/// Beyond the table there is one state rule, applied by the login handler: a
/// connection logs in once. The client sends several packets in any state, and a
/// stricter table guessed ahead of real traffic would close connections a real
/// client holds.
/// </remarks>
public sealed partial class PacketDispatcher
{
    private readonly Dictionary<uint, Route> _routes;
    private readonly PacketNames _names;
    private readonly ILogger<PacketDispatcher> _logger;

    public PacketDispatcher(
        AccountHandlers accounts,
        CharacterHandlers characters,
        PacketNames names,
        ILogger<PacketDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(characters);

        _names = names;
        _logger = logger;
        _routes = new Dictionary<uint, Route>
        {
            [PqKeepAlive.RegistryId] = Route.Ignored,
            [PcLogging.RegistryId] = Route.Ignored,
            [PqFirstPacket.RegistryId] = Route.Undecoded(accounts.FirstPacketAsync),
            [PqCheckAccount.RegistryId] = Route.To<PqCheckAccount>(PqCheckAccount.TryRead, accounts.CheckAccountAsync),
            [PqCreateAccount.RegistryId] = Route.To<PqCreateAccount>(PqCreateAccount.TryRead, accounts.CreateAccountAsync),
            [PqUserLogin.RegistryId] = Route.To<PqUserLogin>(PqUserLogin.TryRead, accounts.UserLoginAsync),
            [PqExistCharacter.RegistryId] = Route.To<PqExistCharacter>(PqExistCharacter.TryRead, characters.ExistCharacterAsync),
        };
    }

    /// <summary>Whether packets with this registry id are handled or knowingly dropped.</summary>
    public bool Routes(uint registryId) => _routes.ContainsKey(registryId);

    /// <summary>
    /// Handles one packet body. Decoding finishes before this returns its task, so the
    /// caller may reuse <paramref name="body"/>'s memory once the task completes.
    /// </summary>
    public ValueTask DispatchAsync(ClientSession session, ReadOnlySpan<byte> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (body.Length < sizeof(uint))
        {
            LogTooShort(_logger, session.Id, body.Length);
            session.Close(CloseReason.MalformedPacket);
            return ValueTask.CompletedTask;
        }

        var registryId = BinaryPrimitives.ReadUInt32LittleEndian(body);
        var name = _names.Describe(registryId);

        if (!_routes.TryGetValue(registryId, out var route))
        {
            LogUnhandled(_logger, session.Id, name);
            session.Close(CloseReason.UnhandledPacket);
            return ValueTask.CompletedTask;
        }

        LogReceived(_logger, session.Id, name, body.Length);

        var error = route.TryHandle(session, body, cancellationToken, out var handled);
        if (error != PacketError.None)
        {
            LogMalformed(_logger, session.Id, name, error);
            session.Close(CloseReason.MalformedPacket);
            return ValueTask.CompletedTask;
        }

        return handled;
    }

    private abstract class Route
    {
        public static readonly Route Ignored = new IgnoredRoute();

        public static Route To<TPacket>(PacketDecoder<TPacket> decode, PacketHandler<TPacket> handle)
            where TPacket : class => new HandledRoute<TPacket>(decode, handle);

        public static Route Undecoded(UndecodedHandler handle) => new UndecodedRoute(handle);

        public abstract PacketError TryHandle(
            ClientSession session,
            ReadOnlySpan<byte> body,
            CancellationToken cancellationToken,
            out ValueTask handled);
    }

    private sealed class IgnoredRoute : Route
    {
        public override PacketError TryHandle(
            ClientSession session,
            ReadOnlySpan<byte> body,
            CancellationToken cancellationToken,
            out ValueTask handled)
        {
            handled = ValueTask.CompletedTask;
            return PacketError.None;
        }
    }

    private sealed class UndecodedRoute(UndecodedHandler handle) : Route
    {
        public override PacketError TryHandle(
            ClientSession session,
            ReadOnlySpan<byte> body,
            CancellationToken cancellationToken,
            out ValueTask handled)
        {
            handled = handle(session, cancellationToken);
            return PacketError.None;
        }
    }

    private sealed class HandledRoute<TPacket>(PacketDecoder<TPacket> decode, PacketHandler<TPacket> handle) : Route
        where TPacket : class
    {
        public override PacketError TryHandle(
            ClientSession session,
            ReadOnlySpan<byte> body,
            CancellationToken cancellationToken,
            out ValueTask handled)
        {
            var read = decode(body);
            handled = read.Succeeded ? handle(session, read.Packet, cancellationToken) : ValueTask.CompletedTask;
            return read.Error;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Connection {ConnectionId} received {Packet}, {Length} bytes")]
    private static partial void LogReceived(ILogger logger, long connectionId, string packet, int length);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet}, which this server does not handle yet; closing it")]
    private static partial void LogUnhandled(ILogger logger, long connectionId, string packet);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet} with a body its codec refused ({Error}); closing it")]
    private static partial void LogMalformed(ILogger logger, long connectionId, string packet, PacketError error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a {Length}-byte body, too short to hold a registry id; closing it")]
    private static partial void LogTooShort(ILogger logger, long connectionId, int length);
}
