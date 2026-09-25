using Microsoft.Extensions.Logging.Abstractions;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Tests.GameServer;

public sealed class PacketNamesTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("packet-names-");

    [Fact]
    public void ImplementedPacketsAreNamedWithoutAFile()
    {
        var names = PacketNames.Load(file: null, _root.FullName, NullLogger.Instance);

        Assert.Equal("PqUserLogin", names.Describe(PqUserLogin.RegistryId));
        Assert.Equal("0x0BADF00D", names.Describe(0x0BADF00D));
    }

    [Fact]
    public void ARelativeFileIsFoundInAFolderAboveTheContentRoot()
    {
        // As when the server is started from its build output rather than its project.
        var contentRoot = _root.CreateSubdirectory(Path.Combine("src", "bin", "Debug"));
        var data = _root.CreateSubdirectory("data");
        File.WriteAllText(Path.Combine(data.FullName, "names.json"), """{"ids": {"PqSomethingElse": 195948557}}""");

        var names = PacketNames.Load(Path.Combine("data", "names.json"), contentRoot.FullName, NullLogger.Instance);

        Assert.Equal("PqSomethingElse", names.Describe(195948557));
    }

    [Fact]
    public void AMissingFileLeavesTheImplementedNames()
    {
        var names = PacketNames.Load("missing.json", _root.FullName, NullLogger.Instance);

        Assert.Equal(new PacketNames().Count, names.Count);
    }

    [Fact]
    public void AFileNeverRenamesAnImplementedPacket()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "names.json"), $$$"""{"ids": {"SomethingElse": {{{PqUserLogin.RegistryId}}}}}""");

        var names = PacketNames.Load("names.json", _root.FullName, NullLogger.Instance);

        Assert.Equal("PqUserLogin", names.Describe(PqUserLogin.RegistryId));
    }

    public void Dispose() => _root.Delete(recursive: true);
}
