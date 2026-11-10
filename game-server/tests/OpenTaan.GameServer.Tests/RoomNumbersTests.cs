using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>How rooms are numbered.</summary>
public sealed class RoomNumbersTests
{
    [Fact]
    public void NumbersRiseFromOne()
    {
        var numbers = new RoomNumbers();

        Assert.Equal((ushort)1, numbers.Take(_ => false));
        Assert.Equal((ushort)2, numbers.Take(_ => false));
        Assert.Equal((ushort)3, numbers.Take(_ => false));
    }

    [Fact]
    public void ANumberInUseIsPassedOver()
    {
        var numbers = new RoomNumbers();

        Assert.Equal((ushort)3, numbers.Take(number => number is 1 or 2));
        Assert.Equal((ushort)4, numbers.Take(_ => false));
    }

    [Fact]
    public void AFreedNumberIsNotHandedOutAgainAtOnce()
    {
        var numbers = new RoomNumbers();
        var inUse = new HashSet<ushort> { numbers.Take(_ => false)!.Value, numbers.Take(_ => false)!.Value };

        inUse.Remove(1);

        Assert.Equal((ushort)3, numbers.Take(inUse.Contains));
    }

    [Fact]
    public void NumbersWrapAfterTheLastAndNeverNameNoRoomOrAnyRoom()
    {
        var numbers = new RoomNumbers();
        var handedOut = new HashSet<ushort>();

        for (var count = RoomNumbers.First; count <= RoomNumbers.Last; count++)
        {
            Assert.True(handedOut.Add(numbers.Take(_ => false)!.Value));
        }

        Assert.Equal(RoomNumbers.Last - RoomNumbers.First + 1, handedOut.Count);
        Assert.DoesNotContain((ushort)0, handedOut);
        Assert.DoesNotContain(PqGameSessionQuickJoin.AnyRoom, handedOut);
        Assert.Equal(RoomNumbers.First, numbers.Take(_ => false));
    }

    [Fact]
    public void NoNumberIsHandedOutWhenEveryOneIsInUse() =>
        Assert.Null(new RoomNumbers().Take(_ => true));
}
