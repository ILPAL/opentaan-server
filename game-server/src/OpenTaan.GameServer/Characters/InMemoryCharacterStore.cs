using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Characters;

/// <summary>Characters held in memory, gone when the process stops. The store until the database stage.</summary>
public sealed class InMemoryCharacterStore : ICharacterStore
{
    // The first character id the reference sessions record.
    private const uint FirstCharacterId = 1001;

    private readonly Dictionary<uint, Character> _characters = [];
    private readonly Dictionary<string, uint> _names = new(CharacterNames.Comparer);
    private readonly Dictionary<uint, uint> _accounts = [];

    // Each character paid for each match, so that keeping a match again pays no one twice.
    private readonly HashSet<(Guid MatchId, uint CharacterId)> _paid = [];
    private readonly Dictionary<(Guid MatchId, uint CharacterId), CardState> _cards = [];
    private readonly Lock _lock = new();
    private uint _nextId = FirstCharacterId;

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<Character>> FindByAccountAsync(uint accountId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<Character> characters = _accounts.TryGetValue(accountId, out var characterId)
                ? [_characters[characterId]]
                : [];
            return ValueTask.FromResult(characters);
        }
    }

    /// <inheritdoc/>
    public ValueTask<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_names.ContainsKey(name));
        }
    }

    /// <inheritdoc/>
    public ValueTask<CharacterCreation> CreateAsync(
        uint accountId,
        string name,
        Func<uint, Character> build,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(build);

        lock (_lock)
        {
            if (_accounts.ContainsKey(accountId))
            {
                return ValueTask.FromResult(new CharacterCreation(CharacterCreationOutcome.AccountHasCharacter, null));
            }

            if (_names.ContainsKey(name))
            {
                return ValueTask.FromResult(new CharacterCreation(CharacterCreationOutcome.NameTaken, null));
            }

            var character = build(_nextId);
            if (character.CharacterId != _nextId || character.AccountId != accountId || character.Name != name)
            {
                throw new InvalidOperationException("A character must be built with the id, account and name it is created under.");
            }

            _nextId++;
            _characters.Add(character.CharacterId, character);
            _names.Add(name, character.CharacterId);
            _accounts.Add(accountId, character.CharacterId);
            return ValueTask.FromResult(new CharacterCreation(CharacterCreationOutcome.Created, character));
        }
    }

    /// <inheritdoc/>
    public ValueTask<bool> DeleteAsync(uint accountId, uint characterId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_characters.TryGetValue(characterId, out var character) || character.AccountId != accountId)
            {
                return ValueTask.FromResult(false);
            }

            _characters.Remove(characterId);
            _names.Remove(character.Name);
            _accounts.Remove(accountId);
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public ValueTask<bool> FinishTutorialAsync(uint characterId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_characters.TryGetValue(characterId, out var character) || character.HasFinishedTutorial)
            {
                return ValueTask.FromResult(false);
            }

            _characters[characterId] = character with { HasFinishedTutorial = true };
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyDictionary<uint, CharacterProgress>> KeepMatchAsync(MatchRecord match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);

        lock (_lock)
        {
            var kept = new Dictionary<uint, CharacterProgress>();
            foreach (var payment in match.Payments)
            {
                if (!_characters.TryGetValue(payment.CharacterId, out var character))
                {
                    continue;
                }

                if (_paid.Add((match.MatchId, payment.CharacterId)))
                {
                    character = character.WithProgress(
                        CharacterProgress.Of(character.Info).After(payment.Experience, payment.Gold, payment.Won));
                    _characters[payment.CharacterId] = character;
                }

                kept[payment.CharacterId] = CharacterProgress.Of(character.Info);
            }

            foreach (var offer in match.Cards)
            {
                _cards.TryAdd((match.MatchId, offer.CharacterId), new CardState(offer.Kind, offer.Amount, null));
            }

            return ValueTask.FromResult<IReadOnlyDictionary<uint, CharacterProgress>>(kept);
        }
    }

    /// <inheritdoc/>
    public ValueTask<CharacterProgress?> KeepCardAsync(
        Guid matchId,
        uint characterId,
        byte cardIndex,
        CancellationToken cancellationToken)
    {
        if (cardIndex >= PrResultSelectCard.CardCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cardIndex), cardIndex, "A result card is numbered from 0 to 7.");
        }

        lock (_lock)
        {
            if (!_cards.TryGetValue((matchId, characterId), out var card))
            {
                throw new InvalidOperationException($"Match {matchId} has no card offer for character {characterId}.");
            }

            if (card.PickedCardIndex is { } previous && previous != cardIndex)
            {
                throw new InvalidOperationException(
                    $"Character {characterId} in match {matchId} already picked card {previous}, not card {cardIndex}.");
            }

            if (!_characters.TryGetValue(characterId, out var character))
            {
                return ValueTask.FromResult<CharacterProgress?>(null);
            }

            if (card.PickedCardIndex is null)
            {
                if (_cards.Any(entry => entry.Key.MatchId == matchId && entry.Value.PickedCardIndex == cardIndex))
                {
                    throw new InvalidOperationException($"Card {cardIndex} in match {matchId} belongs to another character.");
                }

                var progress = CharacterProgress.Of(character.Info).AfterCard(card.Kind, card.Amount);
                character = character.WithProgress(progress);
                _characters[characterId] = character;
                _cards[(matchId, characterId)] = card with { PickedCardIndex = cardIndex };
            }

            return ValueTask.FromResult<CharacterProgress?>(CharacterProgress.Of(character.Info));
        }
    }

    private readonly record struct CardState(CardRewardKind Kind, uint Amount, byte? PickedCardIndex);
}
