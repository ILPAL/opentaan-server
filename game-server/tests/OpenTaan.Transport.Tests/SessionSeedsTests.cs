using OpenTaan.Transport;

namespace OpenTaan.Tests.Transport;

public sealed class SessionSeedsTests
{
    /// <summary>One increment short of wrapping onto the reserved zero.</summary>
    private const uint LandsOnZero = 0xFEB8C0E7;

    [Fact]
    public void BothDirectionsStartAtTheBaseSeed()
    {
        var seeds = new SessionSeeds(0x3092A13F);

        Assert.Equal(0x3092A13Fu, seeds.Receive);
        Assert.Equal(0x3092A13Fu, seeds.Send);
    }

    [Fact]
    public void EachDirectionAdvancesOnItsOwn()
    {
        var seeds = new SessionSeeds(0x3092A13F);

        seeds.AdvanceReceive();
        seeds.AdvanceReceive();
        seeds.AdvanceSend();

        Assert.Equal(2, seeds.ReceivedFrames);
        Assert.Equal(1, seeds.SentFrames);
        Assert.NotEqual(seeds.Receive, seeds.Send);
        Assert.Equal(OpenTaanCipher.AdvanceSeed(0x3092A13F), seeds.Send);
    }

    [Fact]
    public void ResetReturnsBothDirectionsToTheBaseSeed()
    {
        var seeds = new SessionSeeds(0x3092A13F);
        seeds.AdvanceReceive();
        seeds.AdvanceSend();

        seeds.Reset();

        Assert.Equal(0x3092A13Fu, seeds.Receive);
        Assert.Equal(0x3092A13Fu, seeds.Send);
        Assert.Equal(0, seeds.ReceivedFrames);
        Assert.Equal(0, seeds.SentFrames);
    }

    [Fact]
    public void ACarriedSeedPartsCompanyWithARecomputedOneAtTheFirstWrap()
    {
        // The reason the seed is carried rather than recomputed from a frame count.
        // Both agree until an advance lands on the reserved zero; the correction
        // applies to one and not the other, and they never agree again.
        var seeds = new SessionSeeds(LandsOnZero);
        seeds.AdvanceSend();

        var recomputed = unchecked(LandsOnZero + OpenTaanCipher.SeedIncrement);

        Assert.Equal(1u, seeds.Send);
        Assert.Equal(OpenTaanCipher.PlaintextSeed, recomputed);
        Assert.NotEqual(recomputed, seeds.Send);

        // And the gap persists rather than closing on the next frame.
        seeds.AdvanceSend();
        Assert.NotEqual(unchecked(recomputed + OpenTaanCipher.SeedIncrement), seeds.Send);
    }

    [Fact]
    public void AWrappedSeedIsNeverThePlaintextSeed()
    {
        var seeds = new SessionSeeds(LandsOnZero);

        seeds.AdvanceReceive();

        Assert.NotEqual(OpenTaanCipher.PlaintextSeed, seeds.Receive);
    }
}
