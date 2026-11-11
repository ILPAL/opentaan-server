namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Chance a test decides: every match's seed is <see cref="Seed"/>, and every draw among
/// several things takes the one at <see cref="Draw"/>, the first unless the test says
/// otherwise: a card picked for a player is the first one left, and a random pick of map
/// is played on the first map it draws from. How many things each draw was among is kept.
/// </summary>
internal sealed class ScriptedChance(uint seed) : Random
{
    /// <summary>The seed the next match is laid out from.</summary>
    public uint Seed { get; set; } = seed;

    /// <summary>Which of the things the next draw is among it takes, from 0.</summary>
    public int Draw { get; set; }

    /// <summary>How many things each draw so far was among, in order.</summary>
    public List<int> DrawnAmong { get; } = [];

    public override long NextInt64(long minValue, long maxValue) => Seed;

    public override int Next(int maxValue)
    {
        DrawnAmong.Add(maxValue);
        return Draw;
    }
}
