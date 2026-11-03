using OpenTaan.GameServer.Channels;
using OpenTaan.Transport;

namespace OpenTaan.GameServer.Sessions;

/// <summary>Draws the handshake values for a new connection.</summary>
public delegate HandshakeKeys HandshakeKeySource();

/// <summary>What every <see cref="ClientSession"/> shares.</summary>
/// <param name="Dispatcher">Routes each packet to its handler.</param>
/// <param name="Names">Names packets for logs.</param>
/// <param name="Options">Timeouts and limits.</param>
/// <param name="HandshakeKeys">
/// Where handshake values come from: a cryptographic generator in the server, the
/// recorded values when a test replays a session.
/// </param>
/// <param name="LoggedIn">Which connection each logged-in account is live on.</param>
/// <param name="Channels">The channels, and which one each connection is in.</param>
/// <param name="Logger">The sessions' logger.</param>
public sealed record SessionServices(
    PacketDispatcher Dispatcher,
    PacketNames Names,
    GameServerOptions Options,
    HandshakeKeySource HandshakeKeys,
    LoggedInAccounts LoggedIn,
    ChannelDirectory Channels,
    ILogger<ClientSession> Logger);
