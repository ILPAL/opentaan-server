using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Matches;

/// <summary>Draws result cards at a match's end and builds the eight rewards the client expects.</summary>
public static class CardRewards
{
    /// <summary>
    /// Draws one fixed offer for each character still in a room. A card's amount is a
    /// whole number from the inclusive range bounded by the configured percentages of
    /// that character's match pay and the floor for the card's kind.
    /// </summary>
    /// <param name="characterIds">The character in each of the eight game slots, or null.</param>
    /// <param name="payments">What the match paid all characters that started it.</param>
    /// <param name="options">The card weights, percentages and floors.</param>
    /// <param name="random">The server's random draw.</param>
    public static MatchCardOffer[] Roll(
        IReadOnlyList<uint?> characterIds,
        IReadOnlyList<MatchPayment> payments,
        RewardOptions options,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(random);
        if (characterIds.Count != PcResultSetReward.RewardCount)
        {
            throw new ArgumentException($"A room has {PcResultSetReward.RewardCount} game slots.", nameof(characterIds));
        }

        var weight = (long)options.CardGoldWeight + options.CardExperienceWeight;
        if (options.CardGoldWeight < 0 || options.CardExperienceWeight < 0 || weight <= 0)
        {
            throw new ArgumentException("Card weights must be nonnegative and at least one must be positive.", nameof(options));
        }

        var offers = new List<MatchCardOffer>();
        var seen = new HashSet<uint>();
        for (byte slot = 0; slot < characterIds.Count; ++slot)
        {
            if (characterIds[slot] is not { } characterId)
            {
                continue;
            }

            if (characterId == 0 || !seen.Add(characterId))
            {
                throw new ArgumentException("Each occupied slot must name a distinct character.", nameof(characterIds));
            }

            var payment = payments.SingleOrDefault(candidate => candidate.Slot == slot && candidate.CharacterId == characterId)
                ?? throw new ArgumentException($"Character {characterId} in slot {slot} has no match payment.", nameof(payments));
            var kind = random.NextInt64(weight) < options.CardGoldWeight ? CardRewardKind.Gold : CardRewardKind.Experience;
            var baseAmount = kind == CardRewardKind.Gold ? payment.Gold : payment.Experience;
            var floor = kind == CardRewardKind.Gold ? options.CardGoldFloor : options.CardExperienceFloor;
            var amount = Amount(baseAmount, floor, options.CardMinimumPercent, options.CardMaximumPercent, random);
            offers.Add(new MatchCardOffer(characterId, slot, kind, amount));
        }

        return [.. offers];
    }

    /// <summary>Builds exactly eight non-null rewards; empty slots carry an inert gold card.</summary>
    public static PcResultSetReward PacketOf(IReadOnlyList<uint?> characterIds, IReadOnlyList<MatchCardOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        ArgumentNullException.ThrowIfNull(offers);
        if (characterIds.Count != PcResultSetReward.RewardCount)
        {
            throw new ArgumentException($"A room has {PcResultSetReward.RewardCount} game slots.", nameof(characterIds));
        }

        var bySlot = offers.ToDictionary(offer => offer.Slot);
        if (bySlot.Count != characterIds.Count(character => character.HasValue))
        {
            throw new ArgumentException("Each occupied slot must have exactly one offer.", nameof(offers));
        }

        var rewards = new GameReward[PcResultSetReward.RewardCount];
        for (byte slot = 0; slot < rewards.Length; ++slot)
        {
            if (characterIds[slot] is not { } characterId)
            {
                if (bySlot.ContainsKey(slot))
                {
                    throw new ArgumentException($"Empty slot {slot} has an offer.", nameof(offers));
                }

                rewards[slot] = new GameRewardGold(MatchRules.EmptyRewardName, 0, 0, false, 0);
                continue;
            }

            if (!bySlot.TryGetValue(slot, out var offer) || offer.CharacterId != characterId)
            {
                throw new ArgumentException($"Slot {slot} has no offer for character {characterId}.", nameof(offers));
            }

            rewards[slot] = offer.Kind switch
            {
                CardRewardKind.Gold => new GameRewardGold(MatchRules.GoldRewardName, characterId, offer.Amount, false, 0),
                CardRewardKind.Experience => new GameRewardExp("Exp", characterId, offer.Amount),
                _ => throw new ArgumentOutOfRangeException(nameof(offers), $"Unknown card reward kind {offer.Kind}."),
            };
        }

        return new PcResultSetReward(rewards);
    }

    private static uint Amount(uint paid, uint floor, uint minimumPercent, uint maximumPercent, Random random)
    {
        var low = Math.Max(floor, Scaled(paid, minimumPercent));
        var high = Math.Max(low, Scaled(paid, maximumPercent));
        return (uint)random.NextInt64(low, (long)high + 1);
    }

    private static uint Scaled(uint paid, uint percent) =>
        (uint)Math.Min((ulong)paid * percent / 100, uint.MaxValue);
}
