using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A mission's match, from its host's start to the room waiting again, through real
/// sessions on loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more is
/// on its way. A request the match's rules do not allow is dropped, so for one of those,
/// that nothing is sent is what is checked.
/// </remarks>
public sealed class MatchFlowTests
{
    private const uint Seed = 0x5EED5EED;

    [Fact]
    public async Task AHostStartsAMissionOnceEveryGuestIsReadyAndEveryoneIsToldHowEachCharacterStarts()
    {
        var services = Services(random: new ScriptedChance(Seed));
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);

        var start = await StartedAsync(host, host, guest);

        // The mission every new mission room plays, one entry for each character and none
        // for the room's empty or closed slots, and the reference sessions' starting values.
        Assert.Equal(
            new PcGameSessionStartGame(
                MapId: 0,
                Players:
                [
                    new GameSlotPlayInfo(0, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                    new GameSlotPlayInfo(1, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                ],
                Wind: 0,
                Field24: 0,
                CaptainSlots: [0, 1, 0],
                MissionSeed: Seed),
            start);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheLobbyShowsTheRoomPlayingAndNoOneMayEnterIt()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = await MissionRoomAsync(host, guest);
        await EnterLobbyAsync(late);

        await StartedAsync(host, host, guest);

        var change = Assert.Single((await late.ReceiveAsync<PcLobbyUpdateRoomList>(PcLobbyUpdateRoomList.TryRead)).Changes);
        Assert.Equal(RoomStatus.Playing, change.Room!.Status);
        Assert.Equal(EnterRefusal.AlreadyStarted, await RefusedEntryAsync(late, number));
        await QuietAsync(host, guest, late);
    }

    [Fact]
    public async Task AStartIsDroppedWhileAGuestIsNotReadyOrFromAGuestOrInARoomOfTeams()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var teamHost = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host, Requested(gameType: GameType.Mission))).Room.RoomNumber;
        await SeatedAsync(guest, number, host);
        await CreatedAsync(teamHost);

        await host.SendAsync(new PqGameSessionStartGame());
        await QuietAsync(host, guest);

        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(guest, host, guest);
        await guest.SendAsync(new PqGameSessionStartGame());
        await QuietAsync(host, guest);

        // Matches between teams are not played yet; its host alone in it.
        await teamHost.SendAsync(new PqGameSessionStartGame());
        await QuietAsync(host, guest, teamHost);
    }

    [Fact]
    public async Task AHostAloneMayStartAMission()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(host);

        var start = await StartedAsync(host, host);

        Assert.Equal([(byte)0], start.Players.Select(player => player.SlotIndex));
        await QuietAsync(host);
    }

    [Fact]
    public async Task ARandomWeaponIsPlayedAsTheDragonAndAnyOtherAsPicked()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await host.SendAsync(new PqGameSessionChangeWeapon(host.CharacterIds[0], MatchRules.RandomWeapon));
        await WeaponToldAsync(host.CharacterIds[0], MatchRules.RandomWeapon, host, guest);
        await guest.SendAsync(new PqGameSessionChangeWeapon(guest.CharacterIds[0], 0x0102));
        await WeaponToldAsync(guest.CharacterIds[0], 0x0102, host, guest);

        var start = await StartedAsync(host, host, guest);

        Assert.Equal([MatchRules.RandomWeaponPlayedAs, (ushort)0x0102], start.Players.Select(player => player.WeaponId));
    }

    [Fact]
    public async Task EachMatchLaysItsMissionOutFromASeedOfItsOwn()
    {
        var chance = new ScriptedChance(Seed);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(host);
        Assert.Equal(Seed, (await StartedAsync(host, host)).MissionSeed);
        await GiveUpAndEndAsync(host);
        await PickAsync(host, host, card: 2, slot: 0);

        chance.Seed = Seed + 1;
        Assert.Equal(Seed + 1, (await StartedAsync(host, host)).MissionSeed);
    }

    [Fact]
    public async Task EachLevelBeginsOnceForEveryoneHoweverManyClientsAskForIt()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await LevelBegunAsync(1, host, guest);

        // A copy of a request answered already changes nothing.
        await guest.SendAsync(Asking(1));
        await QuietAsync(host, guest);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(PqGameSessionStartMissionLevel.NoNextLevel)]
    public async Task ALevelOtherThanTheNextIsIgnored(byte level)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await host.SendAsync(Asking(level));

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ALevelsFirstTurnWaitsForEveryClientToReportItReady()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);

        await host.SendAsync(new PqGameSessionMissionLevelReady(1));
        await QuietAsync(host, guest);

        await guest.SendAsync(new PqGameSessionMissionLevelReady(1));
        await TurnToldAsync(0, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ALevelsFirstTurnStartsOnceTheWaitIsOverWhenAClientNeverReports()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);
        await host.SendAsync(new PqGameSessionMissionLevelReady(1));
        await QuietAsync(host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().LevelReadyWait - TimeSpan.FromMilliseconds(1));
        await QuietAsync(host, guest);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await TurnToldAsync(0, host, guest);

        // The guest's late report finds the turn begun.
        await guest.SendAsync(new PqGameSessionMissionLevelReady(1));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AReportForALevelNotRunningChangesNothing()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(host);
        await StartedAsync(host, host);

        // Before any level, as the reference sessions do it.
        await host.SendAsync(new PqGameSessionMissionLevelReady(0));
        await QuietAsync(host);

        await LevelBegunAsync(1, host);
        await host.SendAsync(new PqGameSessionMissionLevelReady(2));
        await QuietAsync(host);
    }

    [Fact]
    public async Task ClearingTheLastLevelEndsTheMatchWonWithTheRewardsBeforeTheResult()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);

        // Each level's last enemy falls to a shot of the player whose turn it is, the host and
        // the guest in turn, and the next player has the next level's first turn.
        Player[] shooters = [host, guest];
        for (byte level = 1; level <= 3; level++)
        {
            var slot = (byte)((level - 1) % 2);
            await FirstTurnAsync(level, slot, host, guest);
            await LevelClearedByShotAsync(shooters[slot], slot, (byte)(level + 1), host, guest);
        }

        await FirstTurnAsync(4, 1, host, guest);
        await LevelClearedByShotAsync(guest, 1, PqGameSessionStartMissionLevel.NoNextLevel, host, guest);

        await EndToldAsync(MatchRules.PlayersTeam, [host, guest], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ClearingTheMissionBeforeItsLastLevelIsIgnored()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(host);
        await StartedAsync(host, host);
        await LevelBegunAsync(1, host);

        await host.SendAsync(Asking(PqGameSessionStartMissionLevel.NoNextLevel));

        await QuietAsync(host);
    }

    [Fact]
    public async Task AMatchIsLostOnceEveryPlayerHasGivenUpAndThoseWhoGaveUpSeeItsEnd()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await host.SendAsync(new PcGamePlayGiveUp());
        await QuietAsync(host, guest);

        await guest.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ALevelsFirstTurnGoesToTheLowestCharacterStillInTheMatch()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(new PcGamePlayGiveUp());
        await LevelBegunAsync(1, host, guest);

        // The player who gave up still sees the match, so it is still waited for.
        await guest.SendAsync(new PqGameSessionMissionLevelReady(1));
        await QuietAsync(host, guest);
        await host.SendAsync(new PqGameSessionMissionLevelReady(1));

        await TurnToldAsync(1, host, guest);
    }

    [Fact]
    public async Task NothingAboutAMatchIsAnsweredOnceItIsOver()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);
        await host.SendAsync(new PcGamePlayGiveUp());
        await guest.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);

        await host.SendAsync(new PqGameSessionMissionLevelReady(1));
        await guest.SendAsync(new PqGameSessionMissionLevelReady(1));
        await host.SendAsync(Asking(2));
        await guest.SendAsync(new PcGamePlayGiveUp());
        await host.SendAsync(new PqGameSessionStartGame());
        await guest.SendAsync(new PqGameSessionReady(IsReady: true));

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task APlayerLeavingAMatchIsTakenOffTheFieldThenOutOfTheRoom()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));

        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheHostLeavingAMatchHandsTheRoomOverOnceTheOthersAreToldItLeft()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(host));

        Assert.Equal(new PcGamePlayLeaveUser(0), await guest.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(0), string.Empty),
            await guest.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        Assert.Equal(new PcGameSessionReady(guest.PlayerId, IsReady: false), await guest.ReceiveAsync<PcGameSessionReady>(PcGameSessionReady.TryRead));
        Assert.Equal(guest.PlayerId, (await guest.ReceiveAsync<PcGameSessionUpdateOption>(PcGameSessionUpdateOption.TryRead)).Option.HostPlayerId);
        await QuietAsync(guest);
    }

    [Fact]
    public async Task AClientDisconnectingMidMatchLeavesItAsIfItHadLeft()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await guest.DisposeAsync();
        Assert.Equal(CloseReason.PeerClosed, await guest.Connection.ClosedReason());

        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await QuietAsync(host);
    }

    [Fact]
    public async Task TheLastPlayerInTheMatchLeavingLosesItForThoseWhoGaveUp()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(new PcGamePlayGiveUp());

        await LeaveAsync(guest);

        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        await EndToldAsync(MatchRules.MissionsTeam, [host], host);
        await QuietAsync(host);
    }

    [Fact]
    public async Task ALevelWaitingOnlyForAPlayerWhoLeavesStartsItsTurnWhenThePlayerLeaves()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);
        await host.SendAsync(new PqGameSessionMissionLevelReady(1));

        await LeaveAsync(guest);

        await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead);
        await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        await TurnToldAsync(0, host);
        await QuietAsync(host);
    }

    [Fact]
    public async Task EachCharacterPicksOneCardAndACardTakenFirstIsRefusedToThePickerAlone()
    {
        var services = Services(random: new ScriptedChance(Seed));
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await GiveUpAndEndAsync(host, guest);

        await PickAsync(guest, host, guest, card: 3, slot: 1);

        await host.SendAsync(new PqResultSelectCard(3, 0));
        Assert.Equal(new PrResultAlreadyOpend(3, 0), await host.ReceiveAsync<PrResultAlreadyOpend>(PrResultAlreadyOpend.TryRead));
        await QuietAsync(host, guest);

        // A second card for the guest's character changes nothing.
        await guest.SendAsync(new PqResultSelectCard(5, 1));
        await QuietAsync(host, guest);

        // The host's time runs out: the first card left is picked for it.
        await host.SendAsync(new PqResultRandSelect(0));
        await EveryoneToldAsync(new PrResultSelectCard(0, 0), PrResultSelectCard.TryRead, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ACardRequestNamingAnotherPlayersSlotIsForTheSendersOwnCharacter()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await GiveUpAndEndAsync(host, guest);

        await guest.SendAsync(new PqResultSelectCard(6, 0));

        await EveryoneToldAsync(new PrResultSelectCard(6, 1), PrResultSelectCard.TryRead, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task OnceEveryCharacterHasItsCardTheRoomWaitsAgainAndEveryGuestMustBeReadyAgain()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await GiveUpAndEndAsync(host, guest);
        await PickAsync(host, host, guest, card: 1, slot: 0);
        await PickAsync(guest, host, guest, card: 2, slot: 1);

        // The guest was ready when the match started, and is not any longer.
        await host.SendAsync(new PqGameSessionStartGame());
        await QuietAsync(host, guest);

        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(guest, host, guest);
        await StartedAsync(host, host, guest);
    }

    [Fact]
    public async Task TheRoomWaitsAgainOnceTheResultWaitIsOverWhenAPlayerNeverPicks()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await GiveUpAndEndAsync(host, guest);
        await PickAsync(host, host, guest, card: 1, slot: 0);

        clock.Advance(SessionHarness.ShippedMatchOptions().ResultWait - TimeSpan.FromMilliseconds(1));
        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await QuietAsync(host, guest);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(guest, host, guest);
        Assert.Equal(0, clock.Pending);
    }

    [Fact]
    public async Task BothCharactersOfAConnectionStartTheMatchAndGiveUpTogether()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await MissionRoomAsync(host, pair);

        var start = await StartedAsync(host, host, pair);
        Assert.Equal([(byte)0, (byte)1, (byte)4], start.Players.Select(player => player.SlotIndex));

        await pair.SendAsync(new PcGamePlayGiveUp());
        await QuietAsync(host, pair);
        await host.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(MatchRules.MissionsTeam, [host, pair], host, pair);

        // Each of the pair's characters picks a card of its own.
        await PickAsync(pair, host, pair, card: 4, slot: 1);
        await PickAsync(pair, host, pair, card: 5, slot: 4);
    }

    [Fact]
    public async Task WaitingRoomActionsAreDroppedWhileAMatchIsPlayed()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await guest.SendAsync(new PqGameSessionReady(IsReady: false));
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 5));
        await host.SendAsync(new PqGameSessionUserExpel(guest.CharacterIds[0]));
        await host.SendAsync(new PqGameSessionStartGame());

        await QuietAsync(host, guest);
    }
}
