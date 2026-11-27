using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A character in a match: the player's character at <paramref name="Position"/> among its
/// own, in slot <paramref name="Slot"/>, which <paramref name="GaveUp"/> while it was in play,
/// or played the match to its end.
/// </summary>
internal sealed record SeatOf(Player Player, byte Slot, int Position = 0, bool GaveUp = false);

/// <summary>Brings players into a match and through it, as the client does. Every value is invented.</summary>
internal static class Matching
{
    /// <summary>A request for a mission level, with the values a client sends for one that exists.</summary>
    public static PqGameSessionStartMissionLevel Asking(byte level) => new(level, 0x0102, 0x0304, true);

    /// <summary>Creates a mission room with the host in it, seats each guest, and readies them.</summary>
    public static Task<ushort> MissionRoomAsync(Player host, params Player[] guests) =>
        ReadyRoomAsync(Requested(gameType: GameType.Mission), host, guests);

    /// <summary>
    /// Creates a room of teams with items in play, with the host in it, seats each guest
    /// where the room puts it, on the team with fewer characters, and readies them.
    /// </summary>
    public static Task<ushort> TeamRoomAsync(Player host, params Player[] guests) => TeamRoomAsync(GameMode.Items, host, guests);

    /// <summary>As <see cref="TeamRoomAsync(Player, Player[])"/>, in a room playing <paramref name="gameMode"/>.</summary>
    public static Task<ushort> TeamRoomAsync(GameMode gameMode, Player host, params Player[] guests) =>
        ReadyRoomAsync(Requested(gameMode: gameMode), host, guests);

    /// <summary>As <see cref="TeamRoomAsync(Player, Player[])"/>, in a room playing <paramref name="gameMode"/> with <paramref name="chanceMode"/>.</summary>
    public static Task<ushort> TeamRoomAsync(GameMode gameMode, ChanceMode chanceMode, Player host, params Player[] guests) =>
        ReadyRoomAsync(Requested(gameMode: gameMode, chanceMode: chanceMode), host, guests);

    /// <summary>A report that a client has loaded its match, with its characters in <paramref name="slots"/> where every character starts.</summary>
    public static PcGamePlayLoadingDone Loading(params byte[] slots) =>
        new([.. slots.Select(slot => new LoadedCharacter(slot, MatchRules.StartX, MatchRules.StartY))]);

    /// <summary>
    /// Starts a match between <paramref name="host"/>, in slot 0, and <paramref name="guest"/>,
    /// in slot 2, on the other team; both report it loaded, and the first turn starts: the
    /// host's.
    /// </summary>
    public static async Task AtFirstTeamTurnAsync(Player host, Player guest)
    {
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(Loading(0));
        await guest.SendAsync(Loading(2));
        await TeamTurnToldAsync(0, host, guest);
    }

    /// <summary>
    /// In a match between teams, the player passes the turn of the character in
    /// <paramref name="from"/>: each of <paramref name="players"/> but the passer is passed the
    /// pass, and each is told the turn passes to the character in <paramref name="to"/>.
    /// </summary>
    /// <returns>The boxes the server placed for the next turn.</returns>
    public static async Task<IReadOnlyList<GameScenarioCommandMakeItemBox>> TeamPassedAsync(Player passer, byte from, byte to, params Player[] players)
    {
        var pass = new PcGamePlayPassTurn(from, 0x0102, 0x0304, 0x01, 0x05060708);
        await passer.SendAsync(pass);
        await EveryoneToldAsync(pass, PcGamePlayPassTurn.TryRead, [.. players.Where(player => player != passer)]);
        return await TeamTurnPassedAsync(from, to, players);
    }

    // Creates the room requested with the host in it, seats each guest, and readies them.
    private static async Task<ushort> ReadyRoomAsync(GameSessionInfo requested, Player host, Player[] guests)
    {
        var number = (await CreatedAsync(host, requested)).Room.RoomNumber;
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

    /// <summary>Each of <paramref name="players"/> gives up, and each is told the mission was lost, with nothing paid.</summary>
    public static async Task GiveUpAndEndAsync(params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(new PcGamePlayGiveUp());
        }

        await EndAfterGivingUpToldAsync(players, players);
    }

    /// <summary>
    /// Each of <paramref name="told"/> is sent the end of a mission that every one of the
    /// characters of <paramref name="characters"/>, seated in the first team's slots in
    /// order, gave up: lost, and nothing paid.
    /// </summary>
    public static Task EndAfterGivingUpToldAsync(Player[] characters, params Player[] told) =>
        EndToldAsync(MatchRules.MissionsTeam, InMission(characters, gaveUp: true), mission: true, told);

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
    /// Each of <paramref name="told"/> is sent the end of a mission <paramref name="winningTeam"/>
    /// won, in which the characters of <paramref name="characters"/>, seated in the first
    /// team's slots in order, played to the end: the cards first, then the result, and each
    /// player is then told its characters' experience and gold.
    /// </summary>
    public static Task EndToldAsync(byte winningTeam, Player[] characters, params Player[] told) =>
        EndToldAsync(winningTeam, InMission(characters, gaveUp: false), mission: true, told);

    /// <summary>
    /// Each of <paramref name="told"/> is sent the end of a match between teams
    /// <paramref name="winningTeam"/> won, in which the characters in <paramref name="seats"/>
    /// are the room's only ones: the cards first, then the result, and each player whose
    /// character played to the end is then told its experience and gold.
    /// </summary>
    public static Task EndToldAsync(byte winningTeam, SeatOf[] seats, params Player[] told) =>
        EndToldAsync(winningTeam, seats, mission: false, told);

    // A match's end, as everyone told is sent it: the cards, which give nothing yet, then
    // the result, with a row for each slot. A character that played to the end is paid
    // what Paying says, and one that gave up nothing. Each player is then told the
    // experience and gold of each of its characters paid, in slot order, as the account
    // slot of the character names it. Every character is expected to start the match as
    // it was created, and to stay on its level.
    private static async Task EndToldAsync(byte winningTeam, SeatOf[] seats, bool mission, Player[] told)
    {
        var characters = new uint?[GameSlotInfo.SlotCount];
        var rows = Enumerable.Range(0, GameSlotInfo.SlotCount).Select(slot => GameResultSlot.Empty((byte)slot)).ToArray();
        var paid = new List<(SeatOf Seat, Earning Earned)>();
        foreach (var seat in seats.OrderBy(seat => seat.Slot))
        {
            var character = seat.Player.Characters[seat.Position];
            characters[seat.Slot] = character.CharacterId;
            if (seat.GaveUp)
            {
                rows[seat.Slot] = GameResultSlot.NothingEarned(seat.Slot, character.Level);
                continue;
            }

            var won = Seating.TeamOf(seat.Slot) == winningTeam;
            var earned = mission
                ? won ? Paying.MissionCleared : Paying.MissionLost
                : won ? Paying.TeamWin : Paying.TeamLoss;
            rows[seat.Slot] = PaidRow(seat.Slot, character, earned);
            paid.Add((seat, earned));
        }

        foreach (var player in told)
        {
            Assert.Equal(MatchRules.RewardsOf(characters), await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead));
            Assert.Equal(
                new PcGameSessionEndGame(new GameResult(winningTeam, rows, WinnerPointDelta: 0, LoserPointDelta: 0), EndGameMode: 0),
                await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead));
        }

        foreach (var (seat, earned) in paid)
        {
            var character = seat.Player.Characters[seat.Position];
            var accountSlot = (byte)seat.Position;
            Assert.Equal(
                new PcUpdateExp(accountSlot, character.Level, character.CurrentExperience + earned.Experience, character.ExperienceFloor, character.ExperienceCeiling),
                await seat.Player.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
            Assert.Equal(PcUpdateMoney.GoldOf(accountSlot, character.Gold + earned.Gold), await seat.Player.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        }
    }

    // The characters of players, seated in the first team's slots in order, as the tests seat them.
    private static SeatOf[] InMission(Player[] players, bool gaveUp)
    {
        var teamOne = Enumerable.Range(0, GameSlotInfo.SlotCount).Where(slot => Seating.TeamOf(slot) == 1).Select(slot => (byte)slot).ToArray();
        var next = 0;
        return [.. players.SelectMany(player => player.Characters.Select((_, position) => new SeatOf(player, teamOne[next++], position, gaveUp)))];
    }

    // The row of a character paid earned that stays on its level: its bar from where it
    // started to where the experience takes it, and the experience and gold shown rising.
    private static GameResultSlot PaidRow(byte slot, CharacterInfo character, Earning earned)
    {
        var after = character.CurrentExperience + earned.Experience;
        Assert.True(after < character.ExperienceCeiling, "These helpers expect no level gained; the reward tests check one.");
        return new GameResultSlot(
            slot,
            character.Level,
            (int)earned.Experience,
            [
                new ExpResult(character.Level, character.CurrentExperience, character.ExperienceFloor, character.ExperienceCeiling),
                new ExpResult(character.Level, after, character.ExperienceFloor, character.ExperienceCeiling),
            ],
            earned.Gold,
            KillStat: 0,
            ForceStat: 0,
            HitRate: 0,
            SupportStat: 0,
            Bonuses: [],
            ForceMedalAward: 0,
            KillMedalAward: 0,
            SupportMedalAward: 0,
            HasReward: true,
            ExpReward: earned.Experience,
            GoldReward: earned.Gold);
    }

    /// <summary>
    /// Each of <paramref name="players"/> is told that the first turn of a match, or of a
    /// mission's level, starts: the turn of the character in <paramref name="slot"/>, at a
    /// turn count of 0.
    /// </summary>
    public static Task TurnToldAsync(byte slot, params Player[] players) =>
        EveryoneToldAsync(TurnOf(slot, turnCount: 0, ghostMoves: []), PcGamePlayTurnStart.TryRead, players);

    /// <summary>
    /// Each of <paramref name="players"/> is told that the first turn of a match between teams
    /// starts, the turn of the character in <paramref name="slot"/>: first the item boxes the
    /// server places for it, in a shot of its own, at least one on a field still empty, then
    /// the start itself, which lets the turn's shot drop one box. Returns the boxes placed,
    /// which every player is told alike.
    /// </summary>
    public static async Task<IReadOnlyList<GameScenarioCommandMakeItemBox>> TeamTurnToldAsync(byte slot, params Player[] players)
    {
        var placed = await BoxesThenTurnAsync(slot, shotBoxes: 1, ghostMoves: [], players);
        Assert.NotEmpty(placed);
        return placed;
    }

    /// <summary>
    /// Each of <paramref name="players"/> is told the turn of the character in <paramref name="from"/>
    /// ends, in a match between teams, then the boxes the server places for the turn of the one
    /// in <paramref name="to"/>, then that its turn starts, with <paramref name="ghostMoves"/>.
    /// Returns the boxes placed.
    /// </summary>
    public static async Task<IReadOnlyList<GameScenarioCommandMakeItemBox>> TeamTurnPassedAsync(byte from, byte to, IReadOnlyList<GhostMove> ghostMoves, params Player[] players)
    {
        IReadOnlyList<GameScenarioCommandMakeItemBox>? placed = null;
        foreach (var player in players)
        {
            Assert.Equal(MatchRules.TurnEndOf(from), await player.ReceiveAsync<PcGamePlayTurnEnd>(PcGamePlayTurnEnd.TryRead));
            var told = await BoxesThenTurnAsync(to, shotBoxes: 1, ghostMoves, player);
            Assert.Equal(placed ?? told, told);
            placed = told;
        }

        return placed!;
    }

    /// <summary>As <see cref="TeamTurnPassedAsync(byte, byte, IReadOnlyList{GhostMove}, Player[])"/>, with no ghost moving.</summary>
    public static Task<IReadOnlyList<GameScenarioCommandMakeItemBox>> TeamTurnPassedAsync(byte from, byte to, params Player[] players) =>
        TeamTurnPassedAsync(from, to, ghostMoves: [], players);

    /// <summary>
    /// Each of <paramref name="players"/> is told the boxes the server places for the turn of
    /// the character in <paramref name="slot"/>, in its own shot, then that the turn starts,
    /// letting its shot drop <paramref name="shotBoxes"/> boxes. Every player is told the same
    /// boxes, each a step naming the turn's character; when the field has no room for any,
    /// the server sends no shot, and the turn just starts.
    /// </summary>
    /// <returns>The boxes placed, none when there was no room.</returns>
    public static async Task<IReadOnlyList<GameScenarioCommandMakeItemBox>> BoxesThenTurnAsync(byte slot, byte shotBoxes, IReadOnlyList<GhostMove> ghostMoves, params Player[] players)
    {
        IReadOnlyList<GameScenarioCommandMakeItemBox>? first = null;
        foreach (var player in players)
        {
            var body = await player.Client.ReceiveBodyAsync();
            IReadOnlyList<GameScenarioCommandMakeItemBox> placed = [];
            if (PcGamePlayScenario.TryRead(body).Packet is { } boxes)
            {
                // For no character's slot, with nothing in it but steps the client plays as boxes.
                Assert.Equal(0xFE, boxes.FiringSlot);
                Assert.Equal(slot, boxes.Scenario.SlotIndex);
                Assert.Empty(boxes.Scenario.Effects);
                Assert.Empty(boxes.Scenario.HitSlots);
                placed = [.. boxes.Scenario.Commands.Select(step => Assert.IsType<GameScenarioCommandMakeItemBox>(step))];
                Assert.NotEmpty(placed);
                Assert.All(placed, made => Assert.Equal((slot, (byte)10, 0u), (made.SlotIndex, made.CommandType, made.TimingTick)));
                body = await player.Client.ReceiveBodyAsync();
            }

            Assert.Equal(first ?? placed, placed);
            first = placed;
            var start = PcGamePlayTurnStart.TryRead(body);
            Assert.True(start.Succeeded, $"expected the start of a turn: {start.Error}");
            Assert.Equal(MatchRules.TurnOf(slot, turnCount: 0, shotBoxes, SessionHarness.ShippedMatchOptions(), ghostMoves), start.Packet);
        }

        return first!;
    }

    /// <summary>The box a step of the server's shot drops, failing the test for any other step.</summary>
    public static ItemBox BoxOf(GameScenarioCommand step)
    {
        var made = Assert.IsType<GameScenarioCommandMakeItemBox>(step);
        return new ItemBox(made.ItemBoxId, made.SpawnX, made.SpawnY);
    }

    /// <summary>The start of a mission's turn for the character in <paramref name="slot"/>, as the shipped settings make it.</summary>
    public static PcGamePlayTurnStart TurnOf(byte slot, ushort turnCount, IReadOnlyList<GhostMove> ghostMoves)
    {
        var options = SessionHarness.ShippedMatchOptions();
        return MatchRules.TurnOf(slot, turnCount, options.ItemBoxesPerTurn, options, ghostMoves);
    }

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
            Assert.Equal(TurnOf(to, turnCount, ghostMoves), await player.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead));
        }
    }

    /// <summary>
    /// After a shot of the character in <paramref name="from"/> in a mission level whose
    /// enemies take turns: each of <paramref name="players"/> is told the turn ends and the
    /// enemies take turn <paramref name="turnCount"/>, each reports it over, and each is told
    /// the turn of the character in <paramref name="to"/> starts at that turn count.
    /// </summary>
    public static Task TurnPassedAfterEnemiesAsync(byte from, byte to, ushort turnCount, params Player[] players) =>
        TurnPassedAfterEnemiesAsync(from, to, turnCount, ghostMoves: [], players);

    /// <summary>As <see cref="TurnPassedAfterEnemiesAsync(byte, byte, ushort, Player[])"/>, the start of the turn carrying <paramref name="ghostMoves"/>.</summary>
    public static async Task TurnPassedAfterEnemiesAsync(byte from, byte to, ushort turnCount, IReadOnlyList<GhostMove> ghostMoves, params Player[] players)
    {
        await EnemiesTurnToldAsync(from, turnCount, players);
        await EnemiesTurnOverAsync(turnCount, players);
        await TurnStartedAsync(to, turnCount, ghostMoves, players);
    }

    /// <summary>Each of <paramref name="players"/> is told the turn of the character in <paramref name="from"/> ends, then that the enemies take turn <paramref name="counter"/>.</summary>
    public static async Task EnemiesTurnToldAsync(byte from, uint counter, params Player[] players)
    {
        foreach (var player in players)
        {
            Assert.Equal(MatchRules.TurnEndOf(from), await player.ReceiveAsync<PcGamePlayTurnEnd>(PcGamePlayTurnEnd.TryRead));
            Assert.Equal(new PqGameSessionStartMissionEnemyTurn(counter), await player.ReceiveAsync<PqGameSessionStartMissionEnemyTurn>(PqGameSessionStartMissionEnemyTurn.TryRead));
        }
    }

    /// <summary>Each of <paramref name="players"/> reports the enemies' turn <paramref name="counter"/> over, once, as the client does.</summary>
    public static async Task EnemiesTurnOverAsync(uint counter, params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(new PcGameSessionFinishMissionEnemyTurn(counter));
        }
    }

    /// <summary>Each of <paramref name="players"/> is told that the turn of the character in <paramref name="to"/> starts at <paramref name="turnCount"/>, with no end of a turn before it.</summary>
    public static Task TurnStartedAsync(byte to, ushort turnCount, IReadOnlyList<GhostMove> ghostMoves, params Player[] players) =>
        EveryoneToldAsync(TurnOf(to, turnCount, ghostMoves), PcGamePlayTurnStart.TryRead, players);

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
