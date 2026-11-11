using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Players acting on a match at the same moment, through real sessions on loopback sockets.
/// Every value is invented.
/// </summary>
/// <remarks>
/// Each race is run many times over, on fresh servers, so that the requests meet in as
/// many orders as the machine produces.
/// </remarks>
public sealed class MatchRaceTests
{
    private const int Rounds = 20;

    [Fact]
    public async Task TwoClientsAskingForTheSameLevelAtOnceAreEachToldOnceThatItBegins()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var services = Services();
            await using var host = await ArriveAsync(services, ChannelId, "Kimi");
            await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
            await MissionRoomAsync(host, guest);
            await StartedAsync(host, host, guest);

            await Task.WhenAll(host.SendAsync(Asking(1)), guest.SendAsync(Asking(1)));

            await EveryoneToldAsync(new PcGameSessionStartMissionLevel(1), PcGameSessionStartMissionLevel.TryRead, host, guest);
            await QuietAsync(host, guest);
        }
    }

    [Fact]
    public async Task TheLastTwoPlayersGivingUpAtOnceEndTheMatchOnce()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var services = Services();
            await using var host = await ArriveAsync(services, ChannelId, "Kimi");
            await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
            await MissionRoomAsync(host, guest);
            await StartedAsync(host, host, guest);

            await Task.WhenAll(host.SendAsync(new PcGamePlayGiveUp()), guest.SendAsync(new PcGamePlayGiveUp()));

            await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);
            await QuietAsync(host, guest);
        }
    }

    [Fact]
    public async Task TwoPlayersPickingTheSameCardAtOnceTurnItOverForOneAndRefuseItToTheOther()
    {
        var winners = new int[2];
        for (var round = 0; round < Rounds; round++)
        {
            var services = Services();
            await using var host = await ArriveAsync(services, ChannelId, "Kimi");
            await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
            await MissionRoomAsync(host, guest);
            await StartedAsync(host, host, guest);
            await GiveUpAndEndAsync(host, guest);

            await Task.WhenAll(host.SendAsync(new PqResultSelectCard(5, 0)), guest.SendAsync(new PqResultSelectCard(5, 1)));

            // Whoever's pick came first has the card, and everyone is told; the other is refused alone.
            var turned = await host.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead);
            Assert.Equal(turned, await guest.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead));
            Assert.Equal(5, turned.CardIndex);
            var (refused, slot) = turned.GameSlotIndex == 0 ? (guest, (byte)1) : (host, (byte)0);
            Assert.Equal(new PrResultAlreadyOpend(5, slot), await refused.ReceiveAsync<PrResultAlreadyOpend>(PrResultAlreadyOpend.TryRead));
            await QuietAsync(host, guest);
            winners[turned.GameSlotIndex == 0 ? 0 : 1]++;
        }

        Assert.Equal(Rounds, winners.Sum());
    }
}
