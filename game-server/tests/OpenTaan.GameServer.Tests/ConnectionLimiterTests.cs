using System.Net;
using OpenTaan.GameServer.Sessions;

namespace OpenTaan.Tests.GameServer;

public sealed class ConnectionLimiterTests
{
    private static readonly IPAddress First = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress Second = IPAddress.Parse("192.0.2.2");

    [Fact]
    public void AnAddressAtItsLimitIsRefusedWhileOthersAreStillAdmitted()
    {
        var limiter = new ConnectionLimiter(maxConnections: 10, maxConnectionsPerAddress: 2);

        Assert.Equal(Admission.Admitted, limiter.TryAdmit(First));
        Assert.Equal(Admission.Admitted, limiter.TryAdmit(First));
        Assert.Equal(Admission.AddressFull, limiter.TryAdmit(First));
        Assert.Equal(Admission.Admitted, limiter.TryAdmit(Second));
    }

    [Fact]
    public void AFullServerRefusesEveryAddress()
    {
        var limiter = new ConnectionLimiter(maxConnections: 2, maxConnectionsPerAddress: 5);

        limiter.TryAdmit(First);
        limiter.TryAdmit(Second);

        Assert.Equal(Admission.ServerFull, limiter.TryAdmit(IPAddress.Parse("192.0.2.3")));
        Assert.Equal(2, limiter.Open);
    }

    [Fact]
    public void AReleasedConnectionMakesRoomAgain()
    {
        var limiter = new ConnectionLimiter(maxConnections: 1, maxConnectionsPerAddress: 1);

        limiter.TryAdmit(First);
        limiter.Release(First);

        Assert.Equal(Admission.Admitted, limiter.TryAdmit(First));
        Assert.Equal(1, limiter.Open);
    }

    [Fact]
    public void ARefusedConnectionTakesNoRoom()
    {
        var limiter = new ConnectionLimiter(maxConnections: 5, maxConnectionsPerAddress: 1);

        limiter.TryAdmit(First);
        limiter.TryAdmit(First);
        limiter.Release(First);

        Assert.Equal(0, limiter.Open);
        Assert.Equal(Admission.Admitted, limiter.TryAdmit(First));
    }
}
