using NetArchTest.Rules;
using OpenTaan.Protocol;
using OpenTaan.Transport;
using TestResult = NetArchTest.Rules.TestResult;

namespace OpenTaan.Architecture.Tests;

/// <summary>
/// Dependency rules between projects. Project references already stop the
/// obvious mistakes; these rules also catch the ones a new package reference
/// would let through.
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void TransportDependsOnNoGameDatabaseOrHostingCode()
    {
        var result = Types.InAssembly(typeof(TransportAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "OpenTaan.Protocol",
                "OpenTaan.Persistence",
                "OpenTaan.Game",
                "Npgsql",
                "Microsoft.Extensions.Hosting")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void ProtocolDependsOnNoTransportGameDatabaseOrHostingCode()
    {
        // Codecs turn bodies into packets and back. Framing and encryption happen
        // on the other side of the body, so neither layer needs the other.
        var result = Types.InAssembly(typeof(ProtocolAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "OpenTaan.Transport",
                "OpenTaan.Persistence",
                "OpenTaan.Game",
                "Npgsql",
                "Microsoft.Extensions.Hosting")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        "Forbidden dependency in: " + string.Join(", ", result.FailingTypeNames ?? []);
}
