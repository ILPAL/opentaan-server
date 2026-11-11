namespace OpenTaan.Tests.GameServer;

/// <summary>Chance a test decides: every match's seed is <see cref="Seed"/>, and every card picked for a player is the first one left.</summary>
internal sealed class ScriptedChance(uint seed) : Random
{
    /// <summary>The seed the next match is laid out from.</summary>
    public uint Seed { get; set; } = seed;

    public override long NextInt64(long minValue, long maxValue) => Seed;

    public override int Next(int maxValue) => 0;
}
