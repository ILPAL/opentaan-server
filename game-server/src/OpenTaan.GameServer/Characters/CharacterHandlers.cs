using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Items;

namespace OpenTaan.GameServer.Characters;

/// <summary>Creating, naming, selecting and deleting characters.</summary>
/// <remarks>
/// <para>
/// The client waits for each answer here with no time limit, its character screen
/// locked, so every request is answered, a refused one included. The one exception is
/// a selection that cannot be honoured: its answer has no way to refuse, so the
/// connection is closed instead.
/// </para>
/// <para>
/// The client stores the characters in a creation or deletion answer at the account
/// slot the answer names, without checking the slot. A slot the connection does not
/// hold can only come from a modified client; it is never echoed, and the answer goes
/// to slot 0 instead.
/// </para>
/// </remarks>
public sealed partial class CharacterHandlers(
    ICharacterStore store,
    IOptions<CharacterOptions> options,
    ILogger<CharacterHandlers> logger)
{
    private const byte SafeSlot = 0;
    private const ushort StartingLevel = 1;

    /// <summary>
    /// Answers whether a character with the name exists. A name no client could have
    /// created is answered no without being looked up.
    /// </summary>
    /// <remarks>
    /// Answered in any state and every time: the client disables its "check name"
    /// button until an answer arrives, for as long as the process runs.
    /// </remarks>
    public async ValueTask ExistCharacterAsync(ClientSession session, PqExistCharacter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var exists = CharacterNames.IsValid(request.CharacterName)
            && await store.IsNameTakenAsync(request.CharacterName, cancellationToken).ConfigureAwait(false);
        session.Send(new PrExistCharacter(exists));
    }

    /// <summary>Answers whether a name can be given to a new character.</summary>
    /// <remarks>
    /// Anything but a valid, free name, asked after login, is answered as taken: the
    /// client shows nothing for any other answer, and leaves an earlier "available"
    /// standing.
    /// </remarks>
    public async ValueTask ConfirmNameAsync(ClientSession session, PqConfirmName request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var available = session.IsLoggedIn
            && CharacterNames.IsValid(request.Name)
            && !await store.IsNameTakenAsync(request.Name, cancellationToken).ConfigureAwait(false);
        session.Send(new PrConfirmName(available ? ConfirmNameStatus.Available : ConfirmNameStatus.Taken));
    }

    /// <summary>Creates a character for one of the connection's accounts.</summary>
    /// <remarks>
    /// The name is checked again here: a real client checks it first, but an answer to
    /// an earlier check can arrive after the name in its box has changed.
    /// </remarks>
    public async ValueTask NewCharacterAsync(ClientSession session, PqNewCharacter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (!Holds(session, request.AccountSlot))
        {
            LogSlotNotHeld(logger, session.Id, nameof(PqNewCharacter), request.AccountSlot);
            session.Send(new PrNewCharacter(NewCharacterStatus.Failed, SafeSlot, Catalog: null));
            return;
        }

        if (!CharacterNames.IsValid(request.Name))
        {
            LogInvalidName(logger, session.Id);
            session.Send(new PrNewCharacter(NewCharacterStatus.Failed, request.AccountSlot, Catalog: null));
            return;
        }

        var account = session.Accounts[request.AccountSlot];
        var creation = await store.CreateAsync(
            account.AccountId,
            request.Name,
            characterId => NewCharacter(characterId, account, request),
            cancellationToken).ConfigureAwait(false);

        switch (creation.Outcome)
        {
            case CharacterCreationOutcome.Created:
                LogCreated(logger, session.Id, creation.Character!.CharacterId, account.AccountId);
                var characters = await store.FindByAccountAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
                session.Send(new PrNewCharacter(NewCharacterStatus.Created, request.AccountSlot, Character.CatalogOf(characters)));
                break;

            case CharacterCreationOutcome.NameTaken:
                session.Send(new PrNewCharacter(NewCharacterStatus.NameTaken, request.AccountSlot, Catalog: null));
                break;

            default:
                LogAccountHasCharacter(logger, session.Id, account.AccountId);
                session.Send(new PrNewCharacter(NewCharacterStatus.Failed, request.AccountSlot, Catalog: null));
                break;
        }
    }

    /// <summary>
    /// Starts play with one character per logged-in account, each of which must belong
    /// to the account in that position.
    /// </summary>
    public async ValueTask SelectCharacterAsync(ClientSession session, PqSelectCharacter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (!session.IsLoggedIn || request.CharacterIds.Count != session.Accounts.Count)
        {
            LogSelectionCount(logger, session.Id, request.CharacterIds.Count, session.Accounts.Count);
            session.Close(CloseReason.InvalidSelection);
            return;
        }

        var selected = new List<Character>(request.CharacterIds.Count);
        for (var slot = 0; slot < request.CharacterIds.Count; slot++)
        {
            var account = session.Accounts[slot];
            var characters = await store.FindByAccountAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
            var character = characters.FirstOrDefault(candidate => candidate.CharacterId == request.CharacterIds[slot]);
            if (character is null)
            {
                LogNotOwned(logger, session.Id, nameof(PqSelectCharacter), request.CharacterIds[slot], account.AccountId);
                session.Close(CloseReason.InvalidSelection);
                return;
            }

            selected.Add(character);
        }

        session.SelectCharacters([.. selected.Select(character => character.CharacterId)]);
        session.Send(new PrSelectCharacter(
            Characters: [.. selected.Select(character => character.Info)],
            ItemInventories: Inventories(selected.Count, InventoryKind.Items),
            EquipmentInventories: Inventories(selected.Count, InventoryKind.Equipment),
            BingoInventories: Inventories(selected.Count, InventoryKind.Bingo),
            // Completed tutorials are not recorded yet, so it is offered; the client
            // offers it only to a character below level 2.
            TutorialFlags: [.. selected.Select(_ => (byte)0)],
            SlotUnlockFlags: [.. selected.Select(_ => (byte)1)]));
    }

    /// <summary>Deletes a character, if it belongs to the account in the slot the request names.</summary>
    /// <remarks>
    /// A deletion always carries the account's characters as they are afterwards: the
    /// client has no other way to learn what is left. Characters in a guild or with a
    /// soul mate will be refused once those exist.
    /// </remarks>
    public async ValueTask DelCharacterAsync(ClientSession session, PqDelCharacter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (!Holds(session, request.AccountSlot))
        {
            LogSlotNotHeld(logger, session.Id, nameof(PqDelCharacter), request.AccountSlot);
            session.Send(new PrDelCharacter(DeleteCharacterStatus.Failed, SafeSlot, Catalog: null));
            return;
        }

        var account = session.Accounts[request.AccountSlot];
        if (!await store.DeleteAsync(account.AccountId, request.CharacterId, cancellationToken).ConfigureAwait(false))
        {
            LogNotOwned(logger, session.Id, nameof(PqDelCharacter), request.CharacterId, account.AccountId);
            session.Send(new PrDelCharacter(DeleteCharacterStatus.Failed, request.AccountSlot, Catalog: null));
            return;
        }

        LogDeleted(logger, session.Id, request.CharacterId, account.AccountId);
        session.ForgetSelection(request.CharacterId);
        var characters = await store.FindByAccountAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
        session.Send(new PrDelCharacter(DeleteCharacterStatus.Deleted, request.AccountSlot, Character.CatalogOf(characters)));
    }

    private static bool Holds(ClientSession session, byte slot) => slot < session.Accounts.Count;

    private Character NewCharacter(uint characterId, Account account, PqNewCharacter request)
    {
        var start = options.Value.Start;
        var info = new CharacterInfo(
            CharacterId: characterId,
            Name: request.Name,
            Level: StartingLevel,
            CurrentExperience: 0,
            ExperienceFloor: 0,
            ExperienceCeiling: start.ExperienceCeiling,
            Gold: start.Gold,
            Gender: account.Gender,
            RankPlacement: start.RankPlacement,
            RankScore: start.RankScore,
            LeagueMoney: start.LeagueMoney,
            SoulPoints: start.SoulPoints,
            Wins: start.Wins,
            Losses: start.Losses,
            ForceMedals: start.ForceMedals,
            KillMedals: start.KillMedals,
            SupportMedals: start.SupportMedals,
            GuildName: string.Empty);

        // The two fields the client does not read while drawing carry the character's
        // id and level, as the reference sessions record them.
        var appearance = new CharacterAppear(account.Gender, characterId, StartingLevel, CreatorChoices(request.Appearance));
        return new Character(account.AccountId, info, appearance);
    }

    private AppearanceSlot[] CreatorChoices(IReadOnlyList<AppearanceSlot> requested)
    {
        var parts = options.Value.CreatorParts;
        return [.. requested.Select((slot, position) => parts.Contains(position) ? slot : default)];
    }

    // Each of a character's inventories, numbered as the client is told them: three per
    // account slot, in slot order.
    private static Inventory[] Inventories(int count, InventoryKind kind) =>
        [.. Enumerable.Range(0, count).Select(slot => new Inventory((uint)((slot * 3) + (int)kind), 0, 0))];

    private enum InventoryKind
    {
        Equipment = 1,
        Items = 2,
        Bingo = 3,
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet} for account slot {Slot}, which it does not hold; answered for slot 0")]
    private static partial void LogSlotNotHeld(ILogger logger, long connectionId, string packet, byte slot);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} asked to create a character with a name the client's rules refuse")]
    private static partial void LogInvalidName(ILogger logger, long connectionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} asked to create a second character for account {AccountId}")]
    private static partial void LogAccountHasCharacter(ILogger logger, long connectionId, uint accountId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId} created character {CharacterId} for account {AccountId}")]
    private static partial void LogCreated(ILogger logger, long connectionId, uint characterId, uint accountId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} selected {Count} character(s) with {Accounts} account(s) logged in; closing it")]
    private static partial void LogSelectionCount(ILogger logger, long connectionId, int count, int accounts);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} sent {Packet} for character {CharacterId}, which account {AccountId} does not have")]
    private static partial void LogNotOwned(ILogger logger, long connectionId, string packet, uint characterId, uint accountId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connection {ConnectionId} deleted character {CharacterId} of account {AccountId}")]
    private static partial void LogDeleted(ILogger logger, long connectionId, uint characterId, uint accountId);
}
