using NetArchTest.Rules;
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
    public void Transport_depends_on_no_game_database_or_hosting_code()
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

    private static string Describe(TestResult result) =>
        "Forbidden dependency in: " + string.Join(", ", result.FailingTypeNames ?? []);
}
