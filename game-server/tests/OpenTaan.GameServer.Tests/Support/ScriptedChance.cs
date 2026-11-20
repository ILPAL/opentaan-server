namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Chance a test decides: every match's seed is <see cref="Seed"/>, and every draw among
/// several things takes the one at <see cref="Draw"/>, the first unless the test says
/// otherwise, after any it lines up in <see cref="Draws"/>: a card picked for a player is
/// the first one left, and a random pick of map is played on the first map it draws from.
/// How many things each draw was among is kept.
/// </summary>
internal sealed class ScriptedChance(uint seed) : Random
{
    /// <summary>The seed the next match is laid out from.</summary>
    public uint Seed { get; set; } = seed;

    /// <summary>Which of the things a draw is among it takes, from 0, once <see cref="Draws"/> is empty.</summary>
    public int Draw { get; set; }

    /// <summary>Which of the things each of the next draws is among it takes, from 0, in order.</summary>
    public Queue<int> Draws { get; } = [];

    /// <summary>How many things each draw so far was among, in order.</summary>
    public List<int> DrawnAmong { get; } = [];

    public override long NextInt64(long minValue, long maxValue) => Seed;

    public override int Next(int maxValue)
    {
        DrawnAmong.Add(maxValue);
        return Draws.Count > 0 ? Draws.Dequeue() : Draw;
    }
}
