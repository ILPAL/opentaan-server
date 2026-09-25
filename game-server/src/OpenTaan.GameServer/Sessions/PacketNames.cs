using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpenTaan.Protocol;

namespace OpenTaan.GameServer.Sessions;

/// <summary>Names packets by registry id, for logs.</summary>
/// <remarks>
/// Every class this server implements is named from its <see cref="WireClassAttribute"/>.
/// An optional file can name the rest, which is what makes a log line about an
/// unhandled packet useful; without it such a packet is named by its id.
/// </remarks>
public sealed partial class PacketNames
{
    private readonly Dictionary<uint, string> _names;

    /// <summary>Names the implemented classes, and those in <paramref name="additional"/>.</summary>
    public PacketNames(IReadOnlyDictionary<string, uint>? additional = null)
    {
        _names = typeof(ProtocolAssemblyMarker).Assembly.GetExportedTypes()
            .Select(type => type.GetCustomAttribute<WireClassAttribute>(inherit: false)?.Name)
            .OfType<string>()
            .ToDictionary(RegistryIds.Of);

        foreach (var (name, id) in additional ?? new Dictionary<string, uint>())
        {
            _names.TryAdd(id, name);
        }
    }

    /// <summary>How many packets can be named.</summary>
    public int Count => _names.Count;

    /// <summary>The packet's name, or its id in hexadecimal when it has none here.</summary>
    public string Describe(uint registryId) =>
        _names.TryGetValue(registryId, out var name)
            ? name
            : "0x" + registryId.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>
    /// Names the implemented classes and, when <paramref name="file"/> is found, those it
    /// names. A relative path is looked for in <paramref name="contentRoot"/> and then in
    /// each folder above it, so it is found whichever folder the server is started from.
    /// </summary>
    public static PacketNames Load(string? file, string contentRoot, ILogger logger)
    {
        if (file is null)
        {
            return new PacketNames();
        }

        var found = Locate(file, contentRoot);
        if (found is null)
        {
            LogNotFound(logger, file, contentRoot);
            return new PacketNames();
        }

        var names = new PacketNames(ReadFile(found));
        LogLoaded(logger, names.Count, found);
        return names;
    }

    private static string? Locate(string file, string contentRoot)
    {
        if (Path.IsPathRooted(file))
        {
            return File.Exists(file) ? file : null;
        }

        for (var folder = new DirectoryInfo(contentRoot); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Dictionary<string, uint> ReadFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("ids").EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetUInt32(), StringComparer.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Naming {Count} packets from {Path}")]
    private static partial void LogLoaded(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The packet names file {File} is not in {ContentRoot} or any folder above it; packets this server does not handle are logged by id")]
    private static partial void LogNotFound(ILogger logger, string file, string contentRoot);
}
