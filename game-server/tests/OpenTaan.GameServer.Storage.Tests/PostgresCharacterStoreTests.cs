using Npgsql;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Storage;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Tests.GameServer;

namespace OpenTaan.Tests.Storage;

/// <summary>The character contract, against characters kept in PostgreSQL, and what only the database can show.</summary>
[Collection(nameof(PostgresServer))]
public sealed class PostgresCharacterStoreTests(PostgresServer server) : CharacterStoreContract, IAsyncDisposable
{
    private TestDatabase? _database;

    [Fact]
    public async Task ACharacterBuiltOtherwiseThanItIsKeptIsRefusedAndNothingIsKept()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);

        // Each of these is a value the store works out rather than keeps.
        Func<uint, Character>[] builtOtherwise =
        [
            Kimi(character => character with { Info = character.Info with { Gender = Gender.Female } }),
            Kimi(character => character with
            {
                Info = character.Info with { Gender = Gender.Female },
                Appearance = character.Appearance with { Gender = Gender.Female },
            }),
            Kimi(character => character with { Info = character.Info with { ExperienceCeiling = ExperienceCeiling + 1 } }),
            Kimi(character => character with { Info = character.Info with { GuildName = "Rồng Vàng" } }),
            Kimi(character => character with { Appearance = character.Appearance with { Field08 = character.CharacterId + 1 } }),
            Kimi(character => character with { HasFinishedTutorial = true }),
        ];

        foreach (var build in builtOtherwise)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.CreateAsync(1001, "Kimi", build, Cancel));
        }

        Assert.Empty(await store.FindByAccountAsync(1001, Cancel));
        Assert.False(await store.IsNameTakenAsync("Kimi", Cancel));
    }

    [Fact]
    public async Task ACharacterAtALevelPastTheTableOfLevelsIsRefusedSayingWhichCharacter()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        var created = (await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel)).Character!;
        await _database!.ExecuteAsync($"UPDATE characters SET level = 110 WHERE character_id = {created.CharacterId}", Cancel);

        var problem = await Assert.ThrowsAsync<InvalidDataException>(async () => await store.FindByAccountAsync(1001, Cancel));

        Assert.Contains($"Character {created.CharacterId} is at level 110, past the table of levels", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACharacterTakesItsAccountsGender()
    {
        var store = await NewStoreAsync();
        await _database!.ExecuteAsync(
            "INSERT INTO accounts (account_id, player_id, email, hashed_password, gender) VALUES (1001, 1, 'her@example.test', 'x', 2)",
            Cancel);

        var created = await store.CreateAsync(
            1001,
            "Kimi",
            Kimi(character => character with
            {
                Info = character.Info with { Gender = Gender.Female },
                Appearance = character.Appearance with { Gender = Gender.Female },
            }),
            Cancel);

        Assert.Equal(Gender.Female, Assert.Single(await (await ReopenAsync(store)).FindByAccountAsync(1001, Cancel)).Info.Gender);
        Assert.Equal(CharacterCreationOutcome.Created, created.Outcome);
    }

    [Fact]
    public async Task OnlyThePartsACharacterWearsAreRowsAndTheyGoWithIt()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        var parts = new AppearanceSlot[AppearanceSlot.Count];
        parts[1] = new AppearanceSlot(110_001, 0, 0, 0);
        parts[5] = new AppearanceSlot(150_003, 2, 0, 0);
        var created = (await store.CreateAsync(1001, "Kimi", id => NewCharacter(id, 1001, "Kimi", parts), Cancel)).Character!;

        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM character_parts"));

        await store.DeleteAsync(1001, created.CharacterId, Cancel);
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM character_parts"));
    }

    [Fact]
    public async Task TheDatabaseRefusesASecondCharacterForAnAccountWhoeverWritesIt()
    {
        var store = await NewStoreAsync();
        await GivenAccountsAsync(1001);
        await store.CreateAsync(1001, "Kimi", Builder(1001, "Kimi"), Cancel);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => _database!.ExecuteAsync(
            "INSERT INTO characters (account_id, slot_index, name, name_key) VALUES (1001, 1, 'Rồng', 'RỒNG')", Cancel));

        Assert.Equal("characters_one_per_account", refusal.ConstraintName);
    }

    protected override async Task<ICharacterStore> NewStoreAsync()
    {
        _database = await server.NewDatabaseAsync(Cancel);
        return Store();
    }

    // A store over new connections, as after the server restarts.
    protected override Task<ICharacterStore> ReopenAsync(ICharacterStore store) => Task.FromResult<ICharacterStore>(Store());

    protected override async Task GivenAccountsAsync(params uint[] accountIds)
    {
        foreach (var accountId in accountIds)
        {
            await _database!.ExecuteAsync(
                "INSERT INTO accounts (account_id, player_id, email, hashed_password, gender) " +
                $"VALUES ({accountId}, {accountId}, 'player{accountId}@example.test', 'x', 1)",
                Cancel);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Fact]
    public async Task AMatchIsKeptWithWhatItPaidEachCharacterAndItsRecordOutlivesADeletedOne()
    {
        var store = await NewStoreAsync();
        var (kimi, rong) = await TwoCharactersAsync(store);
        var matchId = Guid.NewGuid();
        MatchPayment[] payments =
        [
            Paid(kimi, 0, won: true, 60, 180),
            new(rong, 2, MatchEnding.Left, Won: false, Experience: 0, Gold: 0),
        ];

        await store.KeepMatchAsync(MatchOf(matchId, payments) with { MissionDifficulty = 2, WinningTeam = 1 }, Cancel);
        Assert.True(await store.DeleteAsync(1002, rong, Cancel));

        Assert.Equal(
            $"{matchId}|7|11|3|2|1|2026-11-27 09:00:00+00|2026-11-27 09:12:00+00",
            await TextAsync(
                "SELECT concat_ws('|', match_id, room_number, channel_id, map_id, mission_difficulty, winning_team, " +
                "to_char(started_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS+00'), to_char(ended_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS+00')) FROM matches"));
        Assert.Equal(
            $"{kimi}|0|played|t|60|180;{rong}|2|left|f|0|0",
            await TextAsync(
                "SELECT string_agg(concat_ws('|', character_id, slot, ending, won, experience, gold), ';' ORDER BY character_id) " +
                $"FROM match_payments WHERE match_id = '{matchId}'"));
    }

    [Fact]
    public async Task GoldAndExperienceStopAtTheMostTheClientCanBeShown()
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);
        await _database!.ExecuteAsync(
            $"UPDATE characters SET level = 109, experience = 18063200, gold = 4294967200, wins = 4294967295 WHERE character_id = {kimi}",
            Cancel);

        var kept = await store.KeepMatchAsync(MatchOf(Guid.NewGuid(), Paid(kimi, 0, won: true, 60, 180)), Cancel);

        Assert.Equal(new CharacterProgress(109, 18063236, uint.MaxValue, uint.MaxValue, 0), kept[kimi]);
    }

    [Theory]
    [InlineData("gave_up", false, 1, 0, "paid_only_for_playing")]
    [InlineData("left", false, 0, 1, "paid_only_for_playing")]
    [InlineData("left", true, 0, 0, "leaving_loses")]
    public async Task TheDatabaseRefusesToPayAPlayerWhoGaveUpOrLeftOrToCountALeaversWin(
        string ending, bool won, int experience, int gold, string constraint)
    {
        var store = await NewStoreAsync();
        var (kimi, _) = await TwoCharactersAsync(store);
        var matchId = Guid.NewGuid();
        await store.KeepMatchAsync(MatchOf(matchId), Cancel);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => _database!.ExecuteAsync(
            "INSERT INTO match_payments (match_id, character_id, slot, ending, won, experience, gold) " +
            $"VALUES ('{matchId}', {kimi}, 0, '{ending}', {won}, {experience}, {gold})",
            Cancel));

        Assert.Equal(constraint, refused.ConstraintName);
    }

    // Account 1001's character Kimi, built as creation builds it and then changed.
    private static Func<uint, Character> Kimi(Func<Character, Character> change) => id => change(NewCharacter(id, 1001, "Kimi"));

    private PostgresCharacterStore Store() =>
        new(_database!.Open());

    private async Task<string?> TextAsync(string sql)
    {
        await using var command = _database!.Open().CreateCommand(sql);
        return (string?)await command.ExecuteScalarAsync(Cancel);
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var command = _database!.Open().CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync(Cancel))!;
    }
}
