namespace OpenTaan.GameServer.Characters;

/// <summary>Characters held in memory, gone when the process stops. The store until the database stage.</summary>
public sealed class InMemoryCharacterStore : ICharacterStore
{
    // The first character id the reference sessions record.
    private const uint FirstCharacterId = 1001;

    private readonly Dictionary<uint, Character> _characters = [];
    private readonly Dictionary<string, uint> _names = new(CharacterNames.Comparer);
    private readonly Dictionary<uint, uint> _accounts = [];
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
}
