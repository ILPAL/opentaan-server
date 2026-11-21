using OpenTaan.GameServer.Characters;

namespace OpenTaan.Tests.Storage;

/// <summary>The character contract, against characters held in memory.</summary>
public sealed class InMemoryCharacterStoreTests : CharacterStoreContract
{
    protected override Task<ICharacterStore> NewStoreAsync() => Task.FromResult<ICharacterStore>(new InMemoryCharacterStore());

    // Memory has no connections to open again: what it holds is what it reads.
    protected override Task<ICharacterStore> ReopenAsync(ICharacterStore store) => Task.FromResult(store);

    // Characters in memory are kept apart from accounts, so any account id will do.
    protected override Task GivenAccountsAsync(params uint[] accountIds) => Task.CompletedTask;
}
