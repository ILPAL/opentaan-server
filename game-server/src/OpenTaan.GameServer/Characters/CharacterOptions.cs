namespace OpenTaan.GameServer.Characters;

/// <summary>Settings bound from the <c>Characters</c> configuration section.</summary>
public sealed class CharacterOptions
{
    public const string Section = "Characters";

    /// <summary>What a new character starts with.</summary>
    public CharacterStart Start { get; set; } = new();

    /// <summary>
    /// The appearance parts the client's character creator lets a player choose, by
    /// position. A new character keeps the request's choices for these parts and
    /// nothing else, so a modified client cannot dress a character it is creating in
    /// items, or give it a partner, that the creator never offers.
    /// </summary>
    /// <remarks>
    /// The creator's choices come from the client's data files, which is why this is a
    /// setting rather than a constant.
    /// </remarks>
    public IReadOnlyList<int> CreatorParts { get; set; } = [];
}

/// <summary>What a new character starts with. Every new character starts at level 1, with no experience.</summary>
public sealed class CharacterStart
{
    /// <summary>
    /// The experience at which level 2 begins. Must be above zero: the client draws the
    /// experience bar by dividing by it, with no guard against zero.
    /// </summary>
    public uint ExperienceCeiling { get; set; }

    /// <summary>Gold.</summary>
    public uint Gold { get; set; }

    /// <summary>Position in the ranking.</summary>
    public uint RankPlacement { get; set; }

    /// <summary>Ranking points.</summary>
    public uint RankScore { get; set; }

    /// <summary>League money.</summary>
    public uint LeagueMoney { get; set; }

    /// <summary>Soul points.</summary>
    public uint SoulPoints { get; set; }

    /// <summary>Matches won.</summary>
    public uint Wins { get; set; }

    /// <summary>Matches lost.</summary>
    public uint Losses { get; set; }

    /// <summary>Force medals.</summary>
    public uint ForceMedals { get; set; }

    /// <summary>Kill medals.</summary>
    public uint KillMedals { get; set; }

    /// <summary>Support medals.</summary>
    public uint SupportMedals { get; set; }
}
