using System.Collections.Concurrent;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;

namespace OpenTaan.Tests.GameServer;

/// <summary>How a <see cref="ScriptedCharacterStore"/> fails a try at keeping a match.</summary>
internal enum KeepFailure
{
    /// <summary>It throws before keeping anything, as a database that cannot be reached.</summary>
    BeforeKeeping,

    /// <summary>It keeps the match, then throws, as a commit whose answer never arrived.</summary>
    AfterKeeping,
}

/// <summary>
/// Characters held in memory, which records every match it is asked to keep, can fail the
/// next tries at keeping one, and knows the account each character was created for.
/// </summary>
internal sealed class ScriptedCharacterStore : ICharacterStore
{
    private readonly InMemoryCharacterStore _characters = new();
    private readonly ConcurrentQueue<KeepFailure> _failures = new();
    private readonly ConcurrentDictionary<string, uint> _accounts = new();

    /// <summary>Every match the store was asked to keep, in order, each try counted.</summary>
    public ConcurrentQueue<MatchRecord> Kept { get; } = new();

    /// <summary>Fails the next tries at keeping a match, one for each of <paramref name="failures"/>, in order.</summary>
    public void FailNext(params KeepFailure[] failures)
    {
        foreach (var failure in failures)
        {
            _failures.Enqueue(failure);
        }
    }

    /// <summary>The character named <paramref name="name"/>, as kept now.</summary>
    public async Task<Character> CharacterAsync(string name) =>
        (await _characters.FindByAccountAsync(_accounts[name], CancellationToken.None)).Single();

    public ValueTask<IReadOnlyList<Character>> FindByAccountAsync(uint accountId, CancellationToken cancellationToken) =>
        _characters.FindByAccountAsync(accountId, cancellationToken);

    public ValueTask<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken) =>
        _characters.IsNameTakenAsync(name, cancellationToken);

    public async ValueTask<CharacterCreation> CreateAsync(uint accountId, string name, Func<uint, Character> build, CancellationToken cancellationToken)
    {
        var creation = await _characters.CreateAsync(accountId, name, build, cancellationToken);
        if (creation.Outcome == CharacterCreationOutcome.Created)
        {
            _accounts[name] = accountId;
        }

        return creation;
    }

    public ValueTask<bool> DeleteAsync(uint accountId, uint characterId, CancellationToken cancellationToken) =>
        _characters.DeleteAsync(accountId, characterId, cancellationToken);

    public ValueTask<bool> FinishTutorialAsync(uint characterId, CancellationToken cancellationToken) =>
        _characters.FinishTutorialAsync(characterId, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<uint, CharacterProgress>> KeepMatchAsync(MatchRecord match, CancellationToken cancellationToken)
    {
        Kept.Enqueue(match);
        var failure = _failures.TryDequeue(out var next) ? next : (KeepFailure?)null;
        if (failure == KeepFailure.BeforeKeeping)
        {
            throw new InvalidOperationException("The store could not be reached.");
        }

        var kept = await _characters.KeepMatchAsync(match, cancellationToken);
        return failure == KeepFailure.AfterKeeping
            ? throw new InvalidOperationException("The store kept the match, and its answer was lost.")
            : kept;
    }
}
