using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Matches;

namespace OpenTaan.Tests.GameServer;

/// <summary>Which of a mission's levels a client may ask for, and when a mission is cleared.</summary>
public sealed class MissionLevelsTests
{
    [Fact]
    public void ALevelBeginsOnlyWhenItIsTheNextOne()
    {
        var levels = new MissionLevels(4);

        Assert.Equal(LevelAnswer.Ignore, levels.Ask(0, out _));
        Assert.Equal(LevelAnswer.Ignore, levels.Ask(2, out _));
        Assert.Equal(0, levels.Current);

        Assert.Equal(LevelAnswer.Announce, levels.Ask(1, out var reason));
        Assert.Null(reason);
        Assert.Equal(1, levels.Current);

        // Every other client asks for the same level.
        Assert.Equal(LevelAnswer.Ignore, levels.Ask(1, out reason));
        Assert.NotNull(reason);
        Assert.Equal(LevelAnswer.Ignore, levels.Ask(3, out _));
        Assert.Equal(LevelAnswer.Announce, levels.Ask(2, out _));
        Assert.Equal(2, levels.Current);
    }

    [Fact]
    public void NoLevelPastTheMissionsLastBegins()
    {
        var levels = new MissionLevels(2);
        levels.Ask(1, out _);
        levels.Ask(2, out _);

        Assert.Equal(LevelAnswer.Ignore, levels.Ask(3, out _));
        Assert.Equal(2, levels.Current);
    }

    [Fact]
    public void AMissionIsClearedOnlyWhileItsLastLevelRuns()
    {
        var levels = new MissionLevels(3);

        Assert.Equal(LevelAnswer.Ignore, levels.Ask(PqGameSessionStartMissionLevel.NoNextLevel, out var early));
        Assert.NotNull(early);

        levels.Ask(1, out _);
        levels.Ask(2, out _);
        Assert.Equal(LevelAnswer.Ignore, levels.Ask(PqGameSessionStartMissionLevel.NoNextLevel, out _));

        levels.Ask(3, out _);
        Assert.Equal(LevelAnswer.Clear, levels.Ask(PqGameSessionStartMissionLevel.NoNextLevel, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void AMissionOfOneLevelIsClearedOnceThatLevelRuns()
    {
        var levels = new MissionLevels(1);

        Assert.Equal(LevelAnswer.Announce, levels.Ask(1, out _));
        Assert.Equal(LevelAnswer.Clear, levels.Ask(PqGameSessionStartMissionLevel.NoNextLevel, out _));
    }
}
