using OpenTaan.Protocol.Accounts;

namespace OpenTaan.GameServer.Accounts;

/// <summary>A game account.</summary>
/// <param name="AccountId">The account's id, as the client is told it at login.</param>
/// <param name="LocalPlayerId">The id the client uses for this player in rooms and games.</param>
/// <param name="EmailAddress">The address the account is known by. Compared exactly, case included.</param>
/// <param name="Password">A hash of the password the client sent when the account was created, never what it sent. Prints as <c>***</c>.</param>
/// <param name="Gender">The gender chosen at creation. Never <see cref="Gender.Unspecified"/>.</param>
/// <param name="Cash">The account's cash, the currency bought with real money.</param>
public sealed record Account(uint AccountId, uint LocalPlayerId, string EmailAddress, StoredPassword Password, Gender Gender, uint Cash);
