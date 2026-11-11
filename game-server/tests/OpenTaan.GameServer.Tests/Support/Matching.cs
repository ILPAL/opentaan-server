using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>Brings players into a mission's match and through it, as the client does. Every value is invented.</summary>
internal static class Matching
{
    /// <summary>A request for a mission level, with the values a client sends for one that exists.</summary>
    public static PqGameSessionStartMissionLevel Asking(byte level) => new(level, 0x0102, 0x0304, true);

    /// <summary>Creates a mission room with the host in it, seats each guest, and readies them.</summary>
    public static async Task<ushort> MissionRoomAsync(Player host, params Player[] guests)
    {
        var number = (await CreatedAsync(host, Requested(gameType: GameType.Mission))).Room.RoomNumber;
        var seated = new List<Player> { host };
        foreach (var guest in guests)
        {
            await SeatedAsync(guest, number, [.. seated]);
            seated.Add(guest);
        }

        foreach (var guest in guests)
        {
            await guest.SendAsync(new PqGameSessionReady(IsReady: true));
            await ReadyToldAsync(guest, [.. seated]);
        }

        return number;
    }

    /// <summary>The host starts its match, and each of <paramref name="players"/> is told the same start.</summary>
    public static async Task<PcGameSessionStartGame> StartedAsync(Player host, params Player[] players)
    {
        await host.SendAsync(new PqGameSessionStartGame());
        var starts = new List<PcGameSessionStartGame>();
        foreach (var player in players)
        {
            starts.Add(await player.ReceiveAsync<PcGameSessionStartGame>(PcGameSessionStartGame.TryRead));
        }

        Assert.All(starts, start => Assert.Equal(starts[0], start));
        return starts[0];
    }

    /// <summary>Each of <paramref name="players"/> asks for <paramref name="level"/>, and each is told once that it begins.</summary>
    public static async Task LevelBegunAsync(byte level, params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(Asking(level));
        }

        await EveryoneToldAsync(new PcGameSessionStartMissionLevel(level), PcGameSessionStartMissionLevel.TryRead, players);
        await QuietAsync(players);
    }

    /// <summary>Each of <paramref name="players"/> gives up, and each is told the mission was lost.</summary>
    public static async Task GiveUpAndEndAsync(params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(new PcGamePlayGiveUp());
        }

        await EndToldAsync(MatchRules.MissionsTeam, players, players);
    }

    /// <summary>The player picks <paramref name="card"/> for the character in <paramref name="slot"/>, and <paramref name="first"/> and <paramref name="second"/> are told.</summary>
    public static async Task PickAsync(Player player, Player first, Player second, byte card, byte slot)
    {
        await player.SendAsync(new PqResultSelectCard(card, slot));
        await EveryoneToldAsync(new PrResultSelectCard(card, slot), PrResultSelectCard.TryRead, first, second);
    }

    /// <summary>The player picks <paramref name="card"/> for the character in <paramref name="slot"/>, and <paramref name="only"/> hears of it.</summary>
    public static async Task PickAsync(Player player, Player only, byte card, byte slot)
    {
        await player.SendAsync(new PqResultSelectCard(card, slot));
        await EveryoneToldAsync(new PrResultSelectCard(card, slot), PrResultSelectCard.TryRead, only);
    }

    /// <summary>
    /// Each of <paramref name="told"/> is sent the end of a match <paramref name="winningTeam"/>
    /// won, in which the characters of <paramref name="characters"/>, seated in the first
    /// team's slots in order, earned nothing: the cards first, then the result.
    /// </summary>
    public static async Task EndToldAsync(byte winningTeam, Player[] characters, params Player[] told)
    {
        var slots = new uint?[GameSlotInfo.SlotCount];
        var levels = new ushort?[GameSlotInfo.SlotCount];
        var teamOne = Enumerable.Range(0, GameSlotInfo.SlotCount).Where(slot => Seating.TeamOf(slot) == 1).ToArray();
        var next = 0;
        foreach (var player in characters)
        {
            foreach (var character in player.Characters)
            {
                // The order of seating in these tests: the first team's lowest slots first.
                var slot = teamOne[next++];
                slots[slot] = character.CharacterId;
                levels[slot] = character.Level;
            }
        }

        foreach (var player in told)
        {
            Assert.Equal(MatchRules.RewardsOf(slots), await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead));
            Assert.Equal(
                new PcGameSessionEndGame(MatchRules.ResultOf(winningTeam, levels), EndGameMode: 0),
                await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead));
        }
    }

    public static Task TurnToldAsync(byte slot, params Player[] players) =>
        EveryoneToldAsync(MatchRules.TurnOf(slot, round: 0), PcGamePlayTurnStart.TryRead, players);

    public static Task ReadyToldAsync(Player guest, params Player[] players) =>
        EveryoneToldAsync(new PcGameSessionReady(guest.PlayerId, IsReady: true), PcGameSessionReady.TryRead, players);

    public static Task WeaponToldAsync(uint characterId, ushort weaponId, params Player[] players) =>
        EveryoneToldAsync(new PcGameSessionChangeWeapon(characterId, weaponId), PcGameSessionChangeWeapon.TryRead, players);

    public static async Task EveryoneToldAsync<TPacket>(TPacket expected, PacketDecoder<TPacket> decode, params Player[] players)
        where TPacket : class
    {
        foreach (var player in players)
        {
            Assert.Equal(expected, await player.ReceiveAsync(decode));
        }
    }
}
