using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.GameServer.Characters;

/// <summary>What a match and a result card change about a character: its level and experience, its gold, and its record.</summary>
/// <param name="Level">The level, the one <paramref name="Experience"/> reaches; see <see cref="LevelTable"/>.</param>
/// <param name="Experience">The experience, a running total.</param>
/// <param name="Gold">The gold.</param>
/// <param name="Wins">The matches won.</param>
/// <param name="Losses">The matches lost.</param>
public readonly record struct CharacterProgress(ushort Level, uint Experience, uint Gold, uint Wins, uint Losses)
{
    /// <summary>The progress <paramref name="info"/> shows.</summary>
    public static CharacterProgress Of(CharacterInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new CharacterProgress(info.Level, info.CurrentExperience, info.Gold, info.Wins, info.Losses);
    }

    /// <summary>
    /// The progress after a match that paid <paramref name="experience"/> and
    /// <paramref name="gold"/>, and that counts as won or lost.
    /// </summary>
    /// <remarks>
    /// The level is the one the new experience reaches. Experience stops at
    /// <see cref="LevelTable.MostExperience"/>, and gold and the record at the most the
    /// client can be sent, so no match can take a count past what it shows.
    /// </remarks>
    public CharacterProgress After(uint experience, uint gold, bool won) =>
        WithExperience(experience) with
        {
            Gold = Added(Gold, gold),
            Wins = won ? Added(Wins, 1) : Wins,
            Losses = won ? Losses : Added(Losses, 1),
        };

    /// <summary>
    /// The progress with <paramref name="experience"/> more experience, at the level it
    /// reaches, and the gold and record as they are. Experience stops at <see cref="LevelTable.MostExperience"/>.
    /// </summary>
    public CharacterProgress WithExperience(uint experience)
    {
        var total = (uint)Math.Min((ulong)Experience + experience, LevelTable.MostExperience);
        return this with { Level = LevelTable.LevelOf(total), Experience = total };
    }

    /// <summary>The progress after a result card, with the match's win/loss record untouched.</summary>
    public CharacterProgress AfterCard(CardRewardKind kind, uint amount) => kind switch
    {
        CardRewardKind.Experience => WithExperience(amount),
        CardRewardKind.Gold => this with { Gold = Added(Gold, amount) },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a result-card reward."),
    };

    private static uint Added(uint count, uint more) => (uint)Math.Min((ulong)count + more, uint.MaxValue);
}
