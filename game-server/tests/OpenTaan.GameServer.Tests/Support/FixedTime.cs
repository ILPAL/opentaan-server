namespace OpenTaan.Tests.GameServer;

/// <summary>A clock stopped at one moment, so a test can say what time a packet carries.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
