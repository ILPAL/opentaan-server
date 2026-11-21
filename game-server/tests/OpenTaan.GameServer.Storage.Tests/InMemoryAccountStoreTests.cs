using OpenTaan.GameServer.Accounts;

namespace OpenTaan.Tests.Storage;

/// <summary>The account contract, against accounts held in memory.</summary>
public sealed class InMemoryAccountStoreTests : AccountStoreContract
{
    protected override Task<IAccountStore> NewStoreAsync() =>
        Task.FromResult<IAccountStore>(new InMemoryAccountStore(StartingCash));

    // Memory has no connections to open again: what it holds is what it reads.
    protected override Task<IAccountStore> ReopenAsync(IAccountStore store) => Task.FromResult(store);
}
