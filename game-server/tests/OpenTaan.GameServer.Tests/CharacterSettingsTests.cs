using OpenTaan.GameServer.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>The character settings, and the experience bar worked out from them.</summary>
public sealed class CharacterSettingsTests
{
    [Fact]
    public void TheFirstLevelsBarRunsFromNothingToTheCeilingTheSettingsName()
    {
        var options = new CharacterOptions { Start = new CharacterStart { ExperienceCeiling = 300 } };

        Assert.Equal(new ExperienceBar(0, 300), options.ExperienceBarAt(Character.StartingLevel));
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)2)]
    [InlineData(ushort.MaxValue)]
    public void NoOtherLevelsBarIsKnownYet(ushort level)
    {
        var options = new CharacterOptions { Start = new CharacterStart { ExperienceCeiling = 300 } };

        Assert.Null(options.ExperienceBarAt(level));
    }
}
