using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Matches;

/// <summary>What a pick of a result card leads to.</summary>
public enum CardOutcome
{
    /// <summary>The card is the character's: everyone is told.</summary>
    Picked,

    /// <summary>Another character took the card first.</summary>
    Taken,

    /// <summary>The character has its card already.</summary>
    AlreadyPicked,

    /// <summary>The card is not one a player may pick.</summary>
    NotACard,
}

/// <summary>The result screen's cards: which character turned over which, each card once.</summary>
/// <remarks>
/// A player picks one card for each of its characters, from the first
/// <see cref="PrResultSelectCard.CardCount"/>, and a card turned over for one character is
/// no one else's. When a player's time runs out, the client asks for a card to be picked
/// for it, from the ones left.
/// </remarks>
public sealed class ResultCards
{
    private readonly Dictionary<byte, byte> _cards = [];

    /// <summary>Picks a card for the character in slot <paramref name="slot"/>.</summary>
    /// <param name="slot">The slot of the character the card is for.</param>
    /// <param name="card">The card the player picked, or null to pick one of those left with <paramref name="random"/>.</param>
    /// <param name="random">Where a card is picked from when the player picked none.</param>
    /// <returns>What the pick leads to, and the card it concerns: the one picked, or the one asked for.</returns>
    public (CardOutcome Outcome, byte Card) Pick(byte slot, byte? card, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (_cards.TryGetValue(slot, out var picked))
        {
            return (CardOutcome.AlreadyPicked, picked);
        }

        if (card is { } asked)
        {
            if (asked >= PrResultSelectCard.CardCount)
            {
                return (CardOutcome.NotACard, asked);
            }

            if (_cards.ContainsValue(asked))
            {
                return (CardOutcome.Taken, asked);
            }

            _cards[slot] = asked;
            return (CardOutcome.Picked, asked);
        }

        // A room has as many slots as there are cards, so one is always left for a character.
        var left = Enumerable.Range(0, PrResultSelectCard.CardCount).Select(free => (byte)free).Where(free => !_cards.ContainsValue(free)).ToArray();
        _cards[slot] = left[random.Next(left.Length)];
        return (CardOutcome.Picked, _cards[slot]);
    }

    /// <summary>Whether the character in slot <paramref name="slot"/> has its card.</summary>
    public bool HasPicked(byte slot) => _cards.ContainsKey(slot);
}
