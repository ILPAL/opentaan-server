using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Choosing a room's captains, in the modes with captains: the host asking for the
/// choice, players volunteering their characters and withdrawing them, and each team's
/// captain drawn as the match starts, through real sessions on loopback sockets. Every
/// value is invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more is
/// on its way. A request dropped is checked by nothing reaching anyone. Two players play
/// one against one: the host in slot 0, on the first team, and the guest in slot 2, on the
/// second. Four play two against two: the host in slot 0, then the next three in slots 2,
/// 1 and 3, so the host and the third player make the first team. Every draw is scripted,
/// and how many characters each was among is checked: the first team's draw, then the
/// second's.
/// </remarks>
public sealed class CaptainFlowTests
{
    [Theory]
    [InlineData(GameMode.CaptainItems)]
    [InlineData(GameMode.CaptainNoItems)]
    public async Task TheHostAsksForTheCaptainsAndEveryoneIsToldTheHostIncludedReadyOrNot(GameMode gameMode)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host, Requested(gameMode: gameMode))).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await ChoosingAsync(host, host, guest);

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task OnlyTheHostAsksForTheCaptains()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);

        await guest.SendAsync(new PqGameSessionChiefDecision());

        await QuietAsync(host, guest);
    }

    [Theory]
    [InlineData(GameMode.Items)]
    [InlineData(GameMode.NoItems)]
    public async Task NoOneAsksForCaptainsInAModeWithoutThem(GameMode gameMode)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(gameMode, host, guest);

        await host.SendAsync(new PqGameSessionChiefDecision());

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task APlayerVolunteersItsOwnCharacterAndWithdrawsItAndEveryoneIsToldEachTime()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);
        await ChoosingAsync(host, host, guest);

        await VolunteeredAsync(guest, 0, host, guest);
        await guest.SendAsync(new PqGameSessionCancelChief(guest.CharacterIds[0]));
        await EveryoneToldAsync(new PcGameSessionCancelChief(guest.CharacterIds[0]), PcGameSessionCancelChief.TryRead, host, guest);

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task VolunteeringIsRefusedToThePlayerAloneOutsideTheChoiceOrForACharacterNotItsOwn()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);

        // Before the host asks for the captains.
        await RefusedAsync(guest, guest.CharacterIds[0]);
        await QuietAsync(host);

        // Another player's character, and one no slot holds.
        await ChoosingAsync(host, host, guest);
        await RefusedAsync(host, guest.CharacterIds[0]);
        await RefusedAsync(host, 0x0A0B0C0D);

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task WithdrawingIsDroppedUnlessTheCharacterIsTheSendersAndVolunteers()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);
        await ChoosingAsync(host, host, guest);

        await guest.SendAsync(new PqGameSessionCancelChief(guest.CharacterIds[0]));
        await QuietAsync(host, guest);

        await VolunteeredAsync(guest, 0, host, guest);
        await host.SendAsync(new PqGameSessionCancelChief(guest.CharacterIds[0]));

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task EachTeamsCaptainIsDrawnFromItsVolunteersAndTheStartNamesBoth()
    {
        var chance = new ScriptedChance(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(GameMode.CaptainItems, host, second, third, fourth);
        await ChoosingAsync(host, everyone);
        await VolunteeredAsync(third, 0, everyone);
        await VolunteeredAsync(fourth, 0, everyone);

        var start = await StartedAsync(host, everyone);

        // The third player in slot 1 captains the first team, the fourth in slot 3 the
        // second, each its team's only volunteer.
        Assert.Equal(
            new PcGameSessionStartGame(
                MapId: 1,
                Players:
                [
                    new GameSlotPlayInfo(0, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                    new GameSlotPlayInfo(1, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                    new GameSlotPlayInfo(2, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                    new GameSlotPlayInfo(3, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                ],
                Wind: 0,
                Field24: 0,
                CaptainSlots: [0xFF, 1, 3],
                MissionSeed: 0),
            start);
        Assert.Equal([1, 1], chance.DrawnAmong);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task OneOfSeveralVolunteersIsDrawnAndOnlyVolunteersAreDrawnFrom()
    {
        var chance = new ScriptedChance(0);
        chance.Draws.Enqueue(1);
        chance.Draws.Enqueue(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(GameMode.CaptainNoItems, host, second, third, fourth);
        await ChoosingAsync(host, everyone);
        await VolunteeredAsync(host, 0, everyone);
        await VolunteeredAsync(third, 0, everyone);
        await VolunteeredAsync(fourth, 0, everyone);

        var start = await StartedAsync(host, everyone);

        // The first team's second volunteer, in slot 1, and the second team's only one, in slot 3.
        Assert.Equal([(byte)0xFF, 1, 3], start.CaptainSlots);
        Assert.Equal([2, 1], chance.DrawnAmong);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task ATeamWithNoVolunteerHasOneOfItsCharactersDrawnEvenWithoutAChoice()
    {
        var chance = new ScriptedChance(0);
        chance.Draws.Enqueue(1);
        chance.Draws.Enqueue(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(GameMode.CaptainItems, host, second, third, fourth);

        var start = await StartedAsync(host, everyone);

        Assert.Equal([(byte)0xFF, 1, 2], start.CaptainSlots);
        Assert.Equal([2, 2], chance.DrawnAmong);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task InAMatchOfOneAgainstOneEachCharacterCaptainsItsTeam()
    {
        var chance = new ScriptedChance(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);
        await ChoosingAsync(host, host, guest);

        var start = await StartedAsync(host, host, guest);

        Assert.Equal([(byte)0xFF, 0, 2], start.CaptainSlots);
        Assert.Equal([1, 1], chance.DrawnAmong);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ATeamWithNoCharacterHasNoCaptainAndNothingIsDrawnForIt()
    {
        var chance = new ScriptedChance(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await TeamRoomAsync(GameMode.CaptainItems, host);

        var start = await StartedAsync(host, host);

        Assert.Equal([(byte)0xFF, 0, 0xFF], start.CaptainSlots);
        Assert.Equal([1], chance.DrawnAmong);
        await QuietAsync(host);
    }

    [Fact]
    public async Task AskingForTheCaptainsAgainForgetsEveryVolunteerBefore()
    {
        var chance = new ScriptedChance(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(GameMode.CaptainItems, host, second, third, fourth);
        await ChoosingAsync(host, everyone);
        await VolunteeredAsync(third, 0, everyone);

        await ChoosingAsync(host, everyone);
        await StartedAsync(host, everyone);

        Assert.Equal([2, 2], chance.DrawnAmong);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task TheStartEndsTheChoiceSoTheRoomWaitingAgainChoosesAfresh()
    {
        var services = Services(random: new ScriptedChance(0));
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);
        await ChoosingAsync(host, host, guest);
        await VolunteeredAsync(guest, 0, host, guest);
        await StartedAsync(host, host, guest);
        await guest.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(1, [new SeatOf(host, 0), new SeatOf(guest, 2)], host, guest);
        await PickAsync(host, host, guest, card: 0, slot: 0);
        await PickAsync(guest, host, guest, card: 1, slot: 2);

        await RefusedAsync(guest, guest.CharacterIds[0]);

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ChangingTheGameModeEndsTheChoice()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.CaptainItems, host, guest);
        await ChoosingAsync(host, host, guest);

        foreach (var gameMode in (GameMode[])[GameMode.Items, GameMode.CaptainItems])
        {
            await host.SendAsync(new PqGameSessionChangeGameOption(7, gameMode, ChanceMode.Basic, 0));
            await EveryoneToldAsync(new PcGameSessionChangeGameOption(7, gameMode, ChanceMode.Basic, 0), PcGameSessionChangeGameOption.TryRead, host, guest);
            await RefusedAsync(guest, guest.CharacterIds[0]);
        }

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AVolunteerWhoLeavesNoLongerVolunteersWhenItComesBack()
    {
        var chance = new ScriptedChance(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        var number = await TeamRoomAsync(GameMode.CaptainItems, host, second, third, fourth);
        await ChoosingAsync(host, everyone);
        await VolunteeredAsync(third, 0, everyone);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(third));
        await EveryoneToldAsync(new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty), PcGameSessionUpdateSlot.TryRead, host, second, fourth);
        await SeatedAsync(third, number, host, second, fourth);
        await third.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(third, everyone);
        await StartedAsync(host, everyone);

        Assert.Equal([2, 2], chance.DrawnAmong);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task TheHostLeavingDuringTheChoiceKeepsEveryoneElsesVolunteering()
    {
        var chance = new ScriptedChance(0);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        await using var fifth = await ArriveAsync(services, ChannelId, "Phong");
        var number = await TeamRoomAsync(GameMode.CaptainItems, host, second, third, fourth);
        await ChoosingAsync(host, host, second, third, fourth);
        await VolunteeredAsync(third, 0, host, second, third, fourth);

        // The third player, in the lowest slot left, hosts the room, and a newcomer takes
        // the host's slot on the first team.
        Assert.Equal(LeaveResult.Left, await LeaveAsync(host));
        foreach (var member in (Player[])[second, third, fourth])
        {
            Assert.Equal(new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(0), string.Empty), await member.ReceiveAsync(PcGameSessionUpdateSlot.TryRead));
            Assert.Equal(new PcGameSessionReady(third.PlayerId, IsReady: false), await member.ReceiveAsync(PcGameSessionReady.TryRead));
            await member.ReceiveAsync(PcGameSessionUpdateOption.TryRead);
        }

        await SeatedAsync(fifth, number, second, third, fourth);
        await fifth.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(fifth, second, third, fourth, fifth);
        var start = await StartedAsync(third, second, third, fourth, fifth);

        Assert.Equal(1, start.CaptainSlots[1]);
        Assert.Equal([1, 2], chance.DrawnAmong);
        await QuietAsync(second, third, fourth, fifth);
    }

    [Fact]
    public async Task BothCharactersOfAConnectionMayVolunteer()
    {
        var chance = new ScriptedChance(0);
        chance.Draws.Enqueue(1);
        chance.Draws.Enqueue(0);
        var services = Services(random: chance);
        await using var first = await ArriveAsync(services, ChannelId, "Kimi", "Linh");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await TeamRoomAsync(GameMode.CaptainItems, first, second);
        await ChoosingAsync(first, first, second);
        await VolunteeredAsync(first, 0, first, second);
        await VolunteeredAsync(first, 1, first, second);
        await VolunteeredAsync(second, 1, first, second);

        var start = await StartedAsync(first, first, second);

        Assert.Equal([(byte)0xFF, 1, 3], start.CaptainSlots);
        Assert.Equal([2, 1], chance.DrawnAmong);
        await QuietAsync(first, second);
    }

    [Fact]
    public async Task ATeammatesGiftToItsCaptainOnTheCaptainsTurnReachesEveryClient()
    {
        var services = Services(random: new ScriptedChance(0), time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(GameMode.CaptainItems, host, second, third, fourth);
        Assert.Equal([(byte)0xFF, 0, 2], (await StartedAsync(host, everyone)).CaptainSlots);
        await host.SendAsync(Loading(0));
        await second.SendAsync(Loading(2));
        await third.SendAsync(Loading(1));
        await fourth.SendAsync(Loading(3));
        await TeamTurnToldAsync(0, everyone);

        // The host captains the first team and has the turn: the third player, its
        // teammate in slot 1, gives it the item in its first item slot.
        var gift = new PcGamePlayUseItem(1, 1, GhostGift: 0, CaptainGift: 1, ItemSlot: 0, ItemId: 0x0203);
        await third.SendAsync(gift);

        await EveryoneToldAsync(gift, PcGamePlayUseItem.TryRead, everyone);
        await QuietAsync(everyone);
    }

    // The host asks for the captains, and each of the players is told.
    private static async Task ChoosingAsync(Player host, params Player[] players)
    {
        await host.SendAsync(new PqGameSessionChiefDecision());
        await EveryoneToldAsync(new PcGameSessionChiefDecision(), PcGameSessionChiefDecision.TryRead, players);
    }

    // The player volunteers its character at position, and each of the players is told.
    private static async Task VolunteeredAsync(Player player, int position, params Player[] players)
    {
        var characterId = player.CharacterIds[position];
        await player.SendAsync(new PqGameSessionChallengeChief(characterId));
        await EveryoneToldAsync(new PcGameSessionChallengeChief(characterId), PcGameSessionChallengeChief.TryRead, players);
    }

    // The player volunteers the character, and it alone is told it was not taken.
    private static async Task RefusedAsync(Player player, uint characterId)
    {
        await player.SendAsync(new PqGameSessionChallengeChief(characterId));
        Assert.Equal(new PcGameSessionFailChallengeChief(characterId), await player.ReceiveAsync(PcGameSessionFailChallengeChief.TryRead));
    }
}
