using System.Buffers.Binary;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Lobby;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Connection;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;

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
/// what the server does with a packet is a decision someone made. The outcomes:
/// <list type="bullet">
///   <item>a packet with a handler is decoded and handled; a body its codec refuses
///   closes the connection, because a request that cannot be read cannot be answered;</item>
///   <item>a request whose layout is not established, and whose contents are not
///   needed, is answered without being decoded;</item>
///   <item>a packet the client sends without expecting an answer, and that this
///   server has no use for yet, is recognised and dropped. Where its layout is known
///   it is read first, so that a body no real client sends still closes the
///   connection;</item>
///   <item>anything else closes the connection. The client never re-sends a request,
///   so one left unanswered stays lost, and some screens stay locked until their
///   reply arrives. A closed connection is at least something the player sees. In
///   development, <see cref="GameServerOptions.DropUnhandledPackets"/> logs and drops
///   it instead, so that one run shows everything a screen sends.</item>
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
    private readonly bool _dropUnhandled;
    private readonly ILogger<PacketDispatcher> _logger;

    public PacketDispatcher(
        AccountHandlers accounts,
        CharacterHandlers characters,
        ChannelHandlers channels,
        LobbyHandlers lobby,
        RoomHandlers rooms,
        MatchHandlers matches,
        PacketNames names,
        IOptions<GameServerOptions> options,
        ILogger<PacketDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(lobby);
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(options);

        _names = names;
        _dropUnhandled = options.Value.DropUnhandledPackets;
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
            [PqConfirmName.RegistryId] = Route.To<PqConfirmName>(PqConfirmName.TryRead, characters.ConfirmNameAsync),
            [PqNewCharacter.RegistryId] = Route.To<PqNewCharacter>(PqNewCharacter.TryRead, characters.NewCharacterAsync),
            [PqSelectCharacter.RegistryId] = Route.To<PqSelectCharacter>(PqSelectCharacter.TryRead, characters.SelectCharacterAsync),
            [PqDelCharacter.RegistryId] = Route.To<PqDelCharacter>(PqDelCharacter.TryRead, characters.DelCharacterAsync),
            [PcUpdateCash.RegistryId] = Route.To<PcUpdateCash>(PcUpdateCash.TryRead, accounts.UpdateCashAsync),
            [PqGetChannelList.RegistryId] = Route.To<PqGetChannelList>(PqGetChannelList.TryRead, channels.GetChannelListAsync),
            [PqChannelMoveTo.RegistryId] = Route.To<PqChannelMoveTo>(PqChannelMoveTo.TryRead, channels.MoveToAsync),
            [PqLobbyEnter.RegistryId] = Route.To<PqLobbyEnter>(PqLobbyEnter.TryRead, lobby.EnterAsync),
            [PqLobbyLeave.RegistryId] = Route.To<PqLobbyLeave>(PqLobbyLeave.TryRead, lobby.LeaveAsync),
            [PcUploadWebImage.RegistryId] = Route.To<PcUploadWebImage>(PcUploadWebImage.TryRead, lobby.UploadPortraitAsync),
            [PqLobbyViewRoomSimpleInfo.RegistryId] = Route.To<PqLobbyViewRoomSimpleInfo>(PqLobbyViewRoomSimpleInfo.TryRead, rooms.RoomInfoAsync),
            [PqGameSessionCreate.RegistryId] = Route.To<PqGameSessionCreate>(PqGameSessionCreate.TryRead, rooms.CreateAsync),
            [PqGameSessionEnter.RegistryId] = Route.To<PqGameSessionEnter>(PqGameSessionEnter.TryRead, rooms.EnterAsync),
            [PqGameSessionQuickJoin.RegistryId] = Route.To<PqGameSessionQuickJoin>(PqGameSessionQuickJoin.TryRead, rooms.QuickJoinAsync),
            [PqGameSessionLeave.RegistryId] = Route.To<PqGameSessionLeave>(PqGameSessionLeave.TryRead, rooms.LeaveAsync),
            [PqGameSessionReady.RegistryId] = Route.To<PqGameSessionReady>(PqGameSessionReady.TryRead, rooms.ReadyAsync),
            [PqGameSessionChangeTeam.RegistryId] = Route.To<PqGameSessionChangeTeam>(PqGameSessionChangeTeam.TryRead, rooms.ChangeTeamAsync),
            [PqGameSessionChangeWeapon.RegistryId] = Route.To<PqGameSessionChangeWeapon>(PqGameSessionChangeWeapon.TryRead, rooms.ChangeWeaponAsync),
            [PqGameSessionChangeGameOption.RegistryId] = Route.To<PqGameSessionChangeGameOption>(PqGameSessionChangeGameOption.TryRead, rooms.ChangeOptionsAsync),
            [PqGameSessionOpenCloseSlot.RegistryId] = Route.To<PqGameSessionOpenCloseSlot>(PqGameSessionOpenCloseSlot.TryRead, rooms.OpenOrCloseSlotAsync),
            [PqGameSessionUserExpel.RegistryId] = Route.To<PqGameSessionUserExpel>(PqGameSessionUserExpel.TryRead, rooms.ExpelAsync),
            [PcTutorialFinished.RegistryId] = Route.To<PcTutorialFinished>(PcTutorialFinished.TryRead, characters.TutorialFinishedAsync),
            [PqGameSessionStartGame.RegistryId] = Route.To<PqGameSessionStartGame>(PqGameSessionStartGame.TryRead, matches.StartGameAsync),
            [PqGameSessionStartMissionLevel.RegistryId] = Route.To<PqGameSessionStartMissionLevel>(PqGameSessionStartMissionLevel.TryRead, matches.StartMissionLevelAsync),
            [PqGameSessionMissionLevelReady.RegistryId] = Route.To<PqGameSessionMissionLevelReady>(PqGameSessionMissionLevelReady.TryRead, matches.MissionLevelReadyAsync),
            [PcGamePlayGiveUp.RegistryId] = Route.To<PcGamePlayGiveUp>(PcGamePlayGiveUp.TryRead, matches.GiveUpAsync),
            [PqResultRandSelect.RegistryId] = Route.To<PqResultRandSelect>(PqResultRandSelect.TryRead, matches.RandSelectAsync),
            [PqResultSelectCard.RegistryId] = Route.To<PqResultSelectCard>(PqResultSelectCard.TryRead, matches.SelectCardAsync),
            [PcGamePlayLoadingDone.RegistryId] = Route.To<PcGamePlayLoadingDone>(PcGamePlayLoadingDone.TryRead, matches.LoadingDoneAsync),
            [PcGamePlayScenario.RegistryId] = Route.To<PcGamePlayScenario>(PcGamePlayScenario.TryRead, matches.ScenarioAsync),
            [PcGamePlayEndScenario.RegistryId] = Route.To<PcGamePlayEndScenario>(PcGamePlayEndScenario.TryRead, matches.EndScenarioAsync),
            [PcGamePlayPassTurn.RegistryId] = Route.To<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead, matches.PassTurnAsync),
            [PcGamePlayMove.RegistryId] = Route.To<PcGamePlayMove>(PcGamePlayMove.TryRead, matches.MoveAsync),
            [PcGamePlaySetPos.RegistryId] = Route.To<PcGamePlaySetPos>(PcGamePlaySetPos.TryRead, matches.SetPosAsync),
            [PcGamePlayDeathByDrop.RegistryId] = Route.To<PcGamePlayDeathByDrop>(PcGamePlayDeathByDrop.TryRead, matches.DeathByDropAsync),
            [PqGamePlayMakeGhost.RegistryId] = Route.To<PqGamePlayMakeGhost>(PqGamePlayMakeGhost.TryRead, matches.MakeGhostAsync),

            // What a client tells of a match it loads, which a mission does not wait on: its
            // enemies, the same ones every client in the room names.
            [PqGameSessionReportEnemies.RegistryId] = Route.ReadAndDropped<PqGameSessionReportEnemies>(PqGameSessionReportEnemies.TryRead),

            [PcGamePlayUseItem.RegistryId] = Route.To<PcGamePlayUseItem>(PcGamePlayUseItem.TryRead, matches.UseItemAsync),
            [PcGamePlaySpendItem.RegistryId] = Route.To<PcGamePlaySpendItem>(PcGamePlaySpendItem.TryRead, matches.SpendItemAsync),
            [PcGamePlayForsakeItem.RegistryId] = Route.To<PcGamePlayForsakeItem>(PcGamePlayForsakeItem.TryRead, matches.ForsakeItemAsync),
            [PcGamePlayForsakeActiveItem.RegistryId] = Route.To<PcGamePlayForsakeActiveItem>(PcGamePlayForsakeActiveItem.TryRead, matches.ForsakeActiveItemAsync),
            [PcGamePlayEatItemBox.RegistryId] = Route.To<PcGamePlayEatItemBox>(PcGamePlayEatItemBox.TryRead, matches.EatItemBoxAsync),
            [PcGamePlayEatBonusItem.RegistryId] = Route.To<PcGamePlayEatBonusItem>(PcGamePlayEatBonusItem.TryRead, matches.EatBonusItemAsync),
            [PcGamePlayNotifyEatItem.RegistryId] = Route.To<PcGamePlayNotifyEatItem>(PcGamePlayNotifyEatItem.TryRead, matches.NotifyEatItemAsync),
            [PcGamePlaySetGhostTarget.RegistryId] = Route.To<PcGamePlaySetGhostTarget>(PcGamePlaySetGhostTarget.TryRead, matches.SetGhostTargetAsync),
            [PcGamePlaySetGhostPos.RegistryId] = Route.To<PcGamePlaySetGhostPos>(PcGamePlaySetGhostPos.TryRead, matches.SetGhostPosAsync),
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
            if (_dropUnhandled)
            {
                LogUnhandledDropped(_logger, session.Id, name, body.Length);
                return ValueTask.CompletedTask;
            }

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

        public static Route ReadAndDropped<TPacket>(PacketDecoder<TPacket> decode)
            where TPacket : class => new HandledRoute<TPacket>(decode, static (_, _, _) => ValueTask.CompletedTask);

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
        Message = "Connection {ConnectionId} sent {Packet}, {Length} bytes, which this server does not handle yet; " +
                  "dropped it, as GameServer:DropUnhandledPackets asks")]
    private static partial void LogUnhandledDropped(ILogger logger, long connectionId, string packet, int length);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet} with a body its codec refused ({Error}); closing it")]
    private static partial void LogMalformed(ILogger logger, long connectionId, string packet, PacketError error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent a {Length}-byte body, too short to hold a registry id; closing it")]
    private static partial void LogTooShort(ILogger logger, long connectionId, int length);
}
