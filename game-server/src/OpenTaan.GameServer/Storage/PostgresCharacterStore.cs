using Npgsql;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Storage;

/// <summary>Characters kept in PostgreSQL, the only copy: every request reads them there and waits for every write.</summary>
/// <remarks>
/// <para>
/// A character is kept as what the server changes about it: its name, its level and
/// counts, the parts it wears, and when it finished the tutorial. The rest of what the
/// client is sent is worked out from those by the rules creation uses: the experience
/// bar from the level, the gender from the account, and the appearance's two fields from
/// the id and the level. A character built any other way is refused, so every character
/// reads back exactly as it was built.
/// </para>
/// <para>
/// The database decides which of two creations takes a name, or an account's one
/// character. Each is a unique index, and the index a refused creation broke says why.
/// </para>
/// <para>
/// A match is kept in one transaction with what it pays. Each payment is a row keyed by the
/// match and the character, and only a row that is new pays: keeping a match again, or
/// keeping it twice at once, finds the rows there and pays no one again. The record of a
/// match stays when a character is deleted, as what that match paid. The card offers are
/// kept with the match; choosing one and crediting it is another transaction, once per
/// character and match.
/// </para>
/// </remarks>
/// <param name="database">The connections to the database.</param>
public sealed class PostgresCharacterStore(NpgsqlDataSource database) : ICharacterStore
{
    private const string NameIndex = "characters_name_key";
    private const string OnePerAccountIndex = "characters_one_per_account";
    private const string OneCharacterPerCard = "one_character_per_card";

    // One statement, so a character and its parts are read at one moment. In the order Read takes them.
    private const string FindByAccountSql =
        "SELECT c.character_id, c.name, c.level, c.experience, c.gold, c.rank_placement, c.rank_score, " +
        "c.league_money, c.soul_points, c.wins, c.losses, c.force_medals, c.kill_medals, c.support_medals, " +
        "c.tutorial_finished_at IS NOT NULL, a.gender, " +
        "p.part, p.item_id, p.variant1, p.variant2, p.variant3 " +
        "FROM characters c " +
        "JOIN accounts a ON a.account_id = c.account_id " +
        "LEFT JOIN character_parts p ON p.character_id = c.character_id " +
        "WHERE c.account_id = $1 " +
        "ORDER BY c.character_id, p.part";

    private const string NameTakenSql = "SELECT EXISTS (SELECT 1 FROM characters WHERE name_key = $1)";

    private const string AccountGenderSql = "SELECT gender FROM accounts WHERE account_id = $1";

    private const string AccountHasCharacterSql = "SELECT EXISTS (SELECT 1 FROM characters WHERE account_id = $1)";

    private const string NextIdSql = "SELECT nextval(pg_get_serial_sequence('characters', 'character_id'))";

    // An account's one character is in its first slot.
    private const string InsertSql =
        "INSERT INTO characters (character_id, account_id, slot_index, name, name_key, level, experience, gold, " +
        "rank_placement, rank_score, league_money, soul_points, wins, losses, force_medals, kill_medals, support_medals) " +
        "VALUES ($1, $2, 0, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16)";

    private const string InsertPartSql =
        "INSERT INTO character_parts (character_id, part, item_id, variant1, variant2, variant3) VALUES ($1, $2, $3, $4, $5, $6)";

    // The parts go with the character: the schema deletes them in the same step.
    private const string DeleteSql = "DELETE FROM characters WHERE character_id = $1 AND account_id = $2";

    // Only the first report changes anything, so a repeat is no news.
    private const string FinishTutorialSql =
        "UPDATE characters SET tutorial_finished_at = now() WHERE character_id = $1 AND tutorial_finished_at IS NULL";

    // A match kept again is the same match: its first record stands.
    private const string InsertMatchSql =
        "INSERT INTO matches (match_id, room_number, channel_id, map_id, mission_difficulty, winning_team, started_at, ended_at) " +
        "VALUES ($1, $2, $3, $4, $5::smallint, $6, $7, $8) ON CONFLICT (match_id) DO NOTHING";

    // Inserted only once for each match and character: the row that is new is the payment.
    private const string InsertPaymentSql =
        "INSERT INTO match_payments (match_id, character_id, slot, ending, won, experience, gold) " +
        "VALUES ($1, $2, $3, $4, $5, $6, $7) ON CONFLICT (match_id, character_id) DO NOTHING";

    // Every offer goes with the match payment kept in the same transaction. The offer is
    // the one already sent to the result screen and cannot be rerolled by a later pick.
    private const string InsertCardSql =
        "INSERT INTO match_cards (match_id, character_id, reward_kind, amount) " +
        "VALUES ($1, $2, $3, $4) ON CONFLICT (match_id, character_id) DO NOTHING";

    private const string CardSql =
        "SELECT reward_kind, amount, picked_card_index FROM match_cards " +
        "WHERE match_id = $1 AND character_id = $2 FOR UPDATE";

    private const string PickCardSql =
        "UPDATE match_cards SET picked_card_index = $3 WHERE match_id = $1 AND character_id = $2";

    // Locked until the transaction ends, so that no other payment comes between the read and the write.
    private const string ProgressSql =
        "SELECT level, experience, gold, wins, losses FROM characters WHERE character_id = $1 FOR UPDATE";

    private const string PaySql =
        "UPDATE characters SET level = $2, experience = $3, gold = $4, wins = $5, losses = $6 WHERE character_id = $1";

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<Character>> FindByAccountAsync(uint accountId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(FindByAccountSql).With((long)accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var characters = new List<Character>(1);
        Kept? kept = null;
        AppearanceSlot[] parts = [];
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var characterId = reader.Unsigned(0);
            if (kept?.CharacterId != characterId)
            {
                if (kept is not null)
                {
                    characters.Add(Describe(accountId, kept, parts));
                }

                kept = Read(reader);
                parts = new AppearanceSlot[AppearanceSlot.Count];
            }

            if (!reader.IsDBNull(16))
            {
                parts[reader.GetInt16(16)] = new AppearanceSlot(
                    reader.Unsigned(17), reader.UnsignedShort(18), reader.UnsignedShort(19), reader.UnsignedShort(20));
            }
        }

        if (kept is not null)
        {
            characters.Add(Describe(accountId, kept, parts));
        }

        return characters;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        await using var command = database.CreateCommand(NameTakenSql).With(CharacterNames.KeyOf(name));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The account does not exist, or the character was built other than under the id,
    /// account and name it is created under, or other than as it is kept.
    /// </exception>
    public async ValueTask<CharacterCreation> CreateAsync(
        uint accountId,
        string name,
        Func<uint, Character> build,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(build);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var gender = await ScalarAsync(connection, transaction, AccountGenderSql, cancellationToken, (long)accountId) is long value
            ? (Gender)value
            : throw new InvalidOperationException($"There is no account {accountId} to create a character for.");

        // Asked first, so that of two reasons to refuse, the one-character rule is the one given.
        if ((bool)(await ScalarAsync(connection, transaction, AccountHasCharacterSql, cancellationToken, (long)accountId))!)
        {
            return new CharacterCreation(CharacterCreationOutcome.AccountHasCharacter, null);
        }

        var key = CharacterNames.KeyOf(name);
        if ((bool)(await ScalarAsync(connection, transaction, NameTakenSql, cancellationToken, key))!)
        {
            return new CharacterCreation(CharacterCreationOutcome.NameTaken, null);
        }

        var characterId = Unsigned((long)(await ScalarAsync(connection, transaction, NextIdSql, cancellationToken))!);
        var character = build(characterId);
        if (character.CharacterId != characterId || character.AccountId != accountId || character.Name != name)
        {
            throw new InvalidOperationException("A character must be built with the id, account and name it is created under.");
        }

        var kept = Keep(character, gender);
        if (character.HasFinishedTutorial || Describe(accountId, kept, character.Appearance.Slots) != character)
        {
            throw new InvalidOperationException(
                "A character must be built as it is kept: its experience bar from its level, its account's gender, no guild, " +
                "its appearance's two fields from its id and level, and the tutorial not yet finished.");
        }

        try
        {
            await ExecuteAsync(connection, transaction, InsertSql, cancellationToken,
                (long)characterId, (long)accountId, name, key, (int)kept.Level, (long)kept.Experience, (long)kept.Gold,
                (long)kept.RankPlacement, (long)kept.RankScore, (long)kept.LeagueMoney, (long)kept.SoulPoints,
                (long)kept.Wins, (long)kept.Losses, (long)kept.ForceMedals, (long)kept.KillMedals, (long)kept.SupportMedals)
                .ConfigureAwait(false);

            var slots = character.Appearance.Slots;
            for (short part = 0; part < slots.Count; part++)
            {
                if (slots[part] != default)
                {
                    await ExecuteAsync(connection, transaction, InsertPartSql, cancellationToken,
                        (long)characterId, part, (long)slots[part].ItemId,
                        (int)slots[part].Variant1, (int)slots[part].Variant2, (int)slots[part].Variant3).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
                                                  && exception.ConstraintName is NameIndex or OnePerAccountIndex)
        {
            // Another creation took the name, or the account's character, since the checks above.
            return new CharacterCreation(
                exception.ConstraintName == NameIndex ? CharacterCreationOutcome.NameTaken : CharacterCreationOutcome.AccountHasCharacter,
                null);
        }

        return new CharacterCreation(CharacterCreationOutcome.Created, character);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeleteAsync(uint accountId, uint characterId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(DeleteSql).With((long)characterId).With((long)accountId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> FinishTutorialAsync(uint characterId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(FinishTutorialSql).With((long)characterId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyDictionary<uint, CharacterProgress>> KeepMatchAsync(MatchRecord match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, InsertMatchSql, cancellationToken,
            match.MatchId, (int)match.RoomNumber, (int)match.ChannelId, (int)match.MapId,
            match.MissionDifficulty is { } difficulty ? (object)(short)difficulty : DBNull.Value,
            (short)match.WinningTeam, match.Started.ToUniversalTime(), match.Ended.ToUniversalTime()).ConfigureAwait(false);

        // In one order, so that two matches paying the same characters lock them alike.
        var kept = new Dictionary<uint, CharacterProgress>();
        foreach (var payment in match.Payments.OrderBy(payment => payment.CharacterId))
        {
            var isNew = await ExecuteAsync(connection, transaction, InsertPaymentSql, cancellationToken,
                match.MatchId, (long)payment.CharacterId, (short)payment.Slot, EndingName(payment.Ending), payment.Won,
                (long)payment.Experience, (long)payment.Gold).ConfigureAwait(false) == 1;

            if (await ProgressAsync(connection, transaction, payment.CharacterId, cancellationToken).ConfigureAwait(false) is not { } progress)
            {
                // Deleted since the match: its record stays, and there is no one to pay.
                continue;
            }

            if (isNew)
            {
                progress = progress.After(payment.Experience, payment.Gold, payment.Won);
                await ExecuteAsync(connection, transaction, PaySql, cancellationToken,
                    (long)payment.CharacterId, (int)progress.Level, (long)progress.Experience, (long)progress.Gold,
                    (long)progress.Wins, (long)progress.Losses).ConfigureAwait(false);
            }

            kept[payment.CharacterId] = progress;
        }

        foreach (var card in match.Cards.OrderBy(card => card.CharacterId))
        {
            await ExecuteAsync(connection, transaction, InsertCardSql, cancellationToken,
                match.MatchId, (long)card.CharacterId, CardKindName(card.Kind), (long)card.Amount).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return kept;
    }

    /// <inheritdoc/>
    public async ValueTask<CharacterProgress?> KeepCardAsync(
        Guid matchId,
        uint characterId,
        byte cardIndex,
        CancellationToken cancellationToken)
    {
        if (cardIndex >= PrResultSelectCard.CardCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cardIndex), cardIndex, "A result card is numbered from 0 to 7.");
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var card = await CardAsync(connection, transaction, matchId, characterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Match {matchId} has no card offer for character {characterId}.");
        if (card.PickedCardIndex is { } previous && previous != cardIndex)
        {
            throw new InvalidOperationException(
                $"Character {characterId} in match {matchId} already picked card {previous}, not card {cardIndex}.");
        }

        if (card.PickedCardIndex is null)
        {
            try
            {
                // Take the card before locking the character: two players contesting the
                // same card cannot hold different character locks while one waits here.
                await ExecuteAsync(connection, transaction, PickCardSql, cancellationToken,
                    matchId, (long)characterId, (short)cardIndex).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                                                      exception.ConstraintName == OneCharacterPerCard)
            {
                throw new InvalidOperationException($"Card {cardIndex} in match {matchId} belongs to another character.", exception);
            }
        }

        var progress = await ProgressAsync(connection, transaction, characterId, cancellationToken).ConfigureAwait(false);
        if (progress is null)
        {
            // The offer remains recorded, but a deleted character cannot be paid. The
            // transaction rolls back the pick, so it never claims payment that did not happen.
            return null;
        }

        if (card.PickedCardIndex is null)
        {
            progress = progress.Value.AfterCard(card.Kind, card.Amount);
            await ExecuteAsync(connection, transaction, PaySql, cancellationToken,
                (long)characterId, (int)progress.Value.Level, (long)progress.Value.Experience, (long)progress.Value.Gold,
                (long)progress.Value.Wins, (long)progress.Value.Losses).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return progress;
    }

    // As the schema names each ending.
    private static string EndingName(MatchEnding ending) => ending switch
    {
        MatchEnding.Played => "played",
        MatchEnding.GaveUp => "gave_up",
        MatchEnding.Left => "left",
        _ => throw new ArgumentOutOfRangeException(nameof(ending), ending, "Not an ending the schema knows."),
    };

    private static string CardKindName(CardRewardKind kind) => kind switch
    {
        CardRewardKind.Gold => "gold",
        CardRewardKind.Experience => "experience",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a result-card reward."),
    };

    private static CardRewardKind CardKind(string kind) => kind switch
    {
        "gold" => CardRewardKind.Gold,
        "experience" => CardRewardKind.Experience,
        _ => throw new InvalidDataException($"Unknown result-card reward {kind}."),
    };

    private static async Task<CardOfferRow?> CardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid matchId,
        uint characterId,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, CardSql, [matchId, (long)characterId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CardOfferRow(
            CardKind(reader.GetString(0)),
            reader.Unsigned(1),
            reader.IsDBNull(2) ? null : checked((byte)reader.GetInt16(2)));
    }

    private readonly record struct CardOfferRow(CardRewardKind Kind, uint Amount, byte? PickedCardIndex);

    private static async Task<CharacterProgress?> ProgressAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        uint characterId,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, ProgressSql, [(long)characterId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CharacterProgress(reader.UnsignedShort(0), reader.Unsigned(1), reader.Unsigned(2), reader.Unsigned(3), reader.Unsigned(4));
    }

    // In the order of FindByAccountSql, up to the parts.
    private static Kept Read(NpgsqlDataReader reader) => new(
        CharacterId: reader.Unsigned(0),
        Name: reader.GetString(1),
        Level: reader.UnsignedShort(2),
        Experience: reader.Unsigned(3),
        Gold: reader.Unsigned(4),
        RankPlacement: reader.Unsigned(5),
        RankScore: reader.Unsigned(6),
        LeagueMoney: reader.Unsigned(7),
        SoulPoints: reader.Unsigned(8),
        Wins: reader.Unsigned(9),
        Losses: reader.Unsigned(10),
        ForceMedals: reader.Unsigned(11),
        KillMedals: reader.Unsigned(12),
        SupportMedals: reader.Unsigned(13),
        FinishedTutorial: reader.GetBoolean(14),
        Gender: (Gender)reader.GetInt64(15));

    private static Kept Keep(Character character, Gender gender)
    {
        var info = character.Info;
        return new Kept(
            info.CharacterId, info.Name, info.Level, info.CurrentExperience, info.Gold, info.RankPlacement, info.RankScore,
            info.LeagueMoney, info.SoulPoints, info.Wins, info.Losses, info.ForceMedals, info.KillMedals, info.SupportMedals,
            character.HasFinishedTutorial, gender);
    }

    // The character the client is sent, from what is kept of it.
    private static Character Describe(uint accountId, Kept kept, IReadOnlyList<AppearanceSlot> parts)
    {
        var bar = LevelTable.BarAt(kept.Level)
            ?? throw new InvalidDataException(
                $"Character {kept.CharacterId} is at level {kept.Level}, past the table of levels.");

        var info = new CharacterInfo(
            kept.CharacterId, kept.Name, kept.Level, kept.Experience, bar.Floor, bar.Ceiling, kept.Gold, kept.Gender,
            kept.RankPlacement, kept.RankScore, kept.LeagueMoney, kept.SoulPoints, kept.Wins, kept.Losses,
            kept.ForceMedals, kept.KillMedals, kept.SupportMedals, GuildName: string.Empty);
        return new Character(accountId, info, Character.AppearanceOf(kept.Gender, kept.CharacterId, kept.Level, parts))
        {
            HasFinishedTutorial = kept.FinishedTutorial,
        };
    }

    private static uint Unsigned(long value) =>
        value is >= 0 and <= uint.MaxValue ? (uint)value : throw new InvalidDataException($"Character id {value} does not fit the client.");

    private static async Task<object?> ScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    // Returns the rows the statement changed.
    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            command.With(value);
        }

        return command;
    }

    // What is kept of a character: everything the server changes about it, and the account's gender.
    private sealed record Kept(
        uint CharacterId,
        string Name,
        ushort Level,
        uint Experience,
        uint Gold,
        uint RankPlacement,
        uint RankScore,
        uint LeagueMoney,
        uint SoulPoints,
        uint Wins,
        uint Losses,
        uint ForceMedals,
        uint KillMedals,
        uint SupportMedals,
        bool FinishedTutorial,
        Gender Gender);
}
