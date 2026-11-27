namespace OpenTaan.GameServer.Matches;

/// <summary>The kind of a result card this server can pay.</summary>
public enum CardRewardKind
{
    /// <summary>Gold added to a character.</summary>
    Gold,

    /// <summary>Experience added to a character.</summary>
    Experience,
}

/// <summary>A result card's reward, fixed for its character when the match ends.</summary>
/// <param name="CharacterId">The character whose card it is.</param>
/// <param name="Slot">The game slot the character occupied when the match ended.</param>
/// <param name="Kind">What the card pays.</param>
/// <param name="Amount">How much it shows and pays when picked.</param>
public sealed record MatchCardOffer(uint CharacterId, byte Slot, CardRewardKind Kind, uint Amount);
