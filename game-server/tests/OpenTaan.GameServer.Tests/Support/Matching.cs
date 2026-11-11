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

    /// <summary>Each of <paramref name="players"/> is told that a level's first turn starts, the turn of the character in <paramref name="slot"/>, at a turn count of 0.</summary>
    public static Task TurnToldAsync(byte slot, params Player[] players) =>
        EveryoneToldAsync(MatchRules.TurnOf(slot, turnCount: 0, SessionHarness.ShippedMatchOptions(), []), PcGamePlayTurnStart.TryRead, players);

    /// <summary>
    /// Starts a mission with <paramref name="host"/> and <paramref name="guest"/>, begins its
    /// first level, and starts its first turn: the host's, in slot 0, with the guest in slot 1.
    /// </summary>
    public static async Task AtFirstTurnAsync(Player host, Player guest)
    {
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);
        await FirstTurnAsync(1, 0, host, guest);
    }

    /// <summary>A shot by the character in <paramref name="slot"/>: a hit, then the mark that ends every shot.</summary>
    public static PcGamePlayScenario Shot(byte slot) => new(
        Direction: 0x01,
        slot,
        new GameScenario(
            slot,
            ActionType: 0x02,
            [new GameScenarioCommandDamage(0x64, 0x04, 0x0102, 0x0304, 0x0506, 0x0708, 0x090A, 0x0B0C), new GameScenarioCommand(slot, 0x08, 0x0D0E, 0)],
            [],
            [],
            WeaponFlags: 0x0F10,
            InitialAngle: 0x11121314,
            [0x64],
            Seed: 0x15161718),
        TotalStats: 0x191A1B1C,
        AmmoAddition: 0x1D1E,
        new IntegrityReport(0x1F202122, 0x23242526, 0x27, 0x28292A2B, 0x2C2D2E2F, 0x30313233));

    /// <summary>Each of <paramref name="players"/> reports <paramref name="level"/> ready, and each is told that the turn of the character in <paramref name="slot"/> starts.</summary>
    public static async Task FirstTurnAsync(byte level, byte slot, params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(new PqGameSessionMissionLevelReady(level));
        }

        await TurnToldAsync(slot, players);
    }

    /// <summary>The shooter shoots for the character in <paramref name="slot"/>, and each of <paramref name="others"/> is passed the shot as it was sent.</summary>
    public static async Task ShotAsync(Player shooter, byte slot, params Player[] others)
    {
        var shot = Shot(slot);
        await shooter.SendAsync(shot);
        await EveryoneToldAsync(shot, PcGamePlayScenario.TryRead, others);
    }

    /// <summary>Each of <paramref name="players"/> reports the shot of the character in <paramref name="slot"/> played back, twice, as the client does.</summary>
    public static async Task FinishedAsync(byte slot, params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(new PcGamePlayEndScenario(slot, 0));
            await player.SendAsync(new PcGamePlayEndScenario(slot, 0));
        }
    }

    /// <summary>
    /// Each of <paramref name="players"/> is told the turn of the character in <paramref name="from"/>
    /// ends, then the turn of the one in <paramref name="to"/> starts, in a level no shot has
    /// counted toward yet.
    /// </summary>
    public static Task TurnPassedAsync(byte from, byte to, params Player[] players) => TurnPassedAsync(from, to, turnCount: 0, ghostMoves: [], players);

    /// <summary>
    /// Each of <paramref name="players"/> is told the turn of the character in <paramref name="from"/>
    /// ends, then the turn of the one in <paramref name="to"/> starts, at turn count
    /// <paramref name="turnCount"/>: the shots the level's players have fired.
    /// </summary>
    public static Task TurnPassedAsync(byte from, byte to, ushort turnCount, params Player[] players) => TurnPassedAsync(from, to, turnCount, ghostMoves: [], players);

    /// <summary>
    /// Each of <paramref name="players"/> is told the turn of the character in <paramref name="from"/>
    /// ends, then the turn of the one in <paramref name="to"/> starts, at turn count
    /// <paramref name="turnCount"/>, with <paramref name="ghostMoves"/>.
    /// </summary>
    public static async Task TurnPassedAsync(byte from, byte to, ushort turnCount, IReadOnlyList<GhostMove> ghostMoves, params Player[] players)
    {
        foreach (var player in players)
        {
            Assert.Equal(MatchRules.TurnEndOf(from), await player.ReceiveAsync<PcGamePlayTurnEnd>(PcGamePlayTurnEnd.TryRead));
            Assert.Equal(MatchRules.TurnOf(to, turnCount, SessionHarness.ShippedMatchOptions(), ghostMoves), await player.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead));
        }
    }

    /// <summary>
    /// The shooter shoots for the character in <paramref name="slot"/>, which kills the level's
    /// last enemy: every one of <paramref name="players"/> asks for <paramref name="next"/> as it
    /// finishes playing the shot back, and is told the level begins once every one has.
    /// </summary>
    public static async Task LevelClearedByShotAsync(Player shooter, byte slot, byte next, params Player[] players)
    {
        await ShotAsync(shooter, slot, [.. players.Where(player => player != shooter)]);
        foreach (var player in players)
        {
            await player.SendAsync(Asking(next));
            await FinishedAsync(slot, player);
        }

        if (next != PqGameSessionStartMissionLevel.NoNextLevel)
        {
            await EveryoneToldAsync(new PcGameSessionStartMissionLevel(next), PcGameSessionStartMissionLevel.TryRead, players);
        }
    }

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
