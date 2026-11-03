using System.Security.Cryptography;
using System.Text;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.GameServer.Accounts;

/// <summary>A game account.</summary>
/// <param name="AccountId">The account's id, as the client is told it at login.</param>
/// <param name="LocalPlayerId">The id the client uses for this player in rooms and games.</param>
/// <param name="EmailAddress">The address the account is known by. Compared exactly, case included.</param>
/// <param name="PasswordHash">The password as the client hashed it when the account was created. Prints as <c>***</c>.</param>
/// <param name="Gender">The gender chosen at creation. Never <see cref="Gender.Unspecified"/>.</param>
/// <param name="Cash">The account's cash, the currency bought with real money.</param>
public sealed record Account(uint AccountId, uint LocalPlayerId, string EmailAddress, Secret PasswordHash, Gender Gender, uint Cash)
{
    /// <summary>Whether <paramref name="presented"/> is this account's password, compared in constant time.</summary>
    public bool HasPassword(Secret presented)
    {
        ArgumentNullException.ThrowIfNull(presented);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(PasswordHash.Reveal()),
            Encoding.UTF8.GetBytes(presented.Reveal()));
    }
}
