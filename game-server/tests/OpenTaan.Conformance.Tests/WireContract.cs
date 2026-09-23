using System.Text.Json;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// The wire contract, loaded from beside the test binaries when it is available.
/// </summary>
/// <remarks>
/// The contract is derived from the game client, so it is supplied locally and never
/// committed (ADR 10). Every test that needs it skips when it is absent, which keeps
/// CI honest: the tests run, say why they could not check anything, and pass.
///
/// Nothing here asserts how much the contract contains. Session and frame counts stay
/// out of the repository along with the contract itself, so the tests describe shape
/// rather than size.
/// </remarks>
public static class WireContract
{
    private static readonly Lazy<Contents> Loaded = new(Load);

    /// <summary>Where the contract is looked for.</summary>
    public static string Location { get; } = Path.Combine(AppContext.BaseDirectory, "contracts", "wire");

    /// <summary>Whether the contract was found and read.</summary>
    public static bool Available => Loaded.Value.Failure is null;

    /// <summary>Why there is nothing to check, for a skip message.</summary>
    public static string Absence => Loaded.Value.Failure ?? "the wire contract is available";

    /// <summary>The declared packet layouts.</summary>
    public static CodecSpec Spec => Present().Spec;

    /// <summary>The registry id of every packet class.</summary>
    public static RegistryTable Registry => Present().Registry;

    /// <summary>Every recorded session.</summary>
    public static IReadOnlyList<Session> Sessions => Present().Sessions;

    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private static Contents Present() =>
        Available ? Loaded.Value : throw new InvalidOperationException(Absence);

    private static Contents Load()
    {
        var spec = Path.Combine(Location, "codec-spec.json");
        var registry = Path.Combine(Location, "registry-ids.json");
        var corpus = Path.Combine(Location, "corpus");

        if (!File.Exists(spec) || !File.Exists(registry) || !Directory.Exists(corpus))
        {
            return new Contents(
                $"the wire contract is not in {Location}. It is derived from the game " +
                "client and supplied locally rather than committed, so these checks only " +
                "run on a machine that has it.");
        }

        var sessions = Directory
            .EnumerateFiles(corpus, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Read<Session>(path) with { Origin = Path.GetFileName(path) })
            .ToArray();

        return new Contents(Read<CodecSpec>(spec), Read<RegistryTable>(registry), sessions);
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Format)
        ?? throw new InvalidOperationException($"{path} is empty");

    private sealed record Contents
    {
        public Contents(CodecSpec spec, RegistryTable registry, IReadOnlyList<Session> sessions) =>
            (Spec, Registry, Sessions) = (spec, registry, sessions);

        public Contents(string failure) =>
            (Failure, Spec, Registry, Sessions) = (failure, null!, null!, null!);

        public string? Failure { get; }

        public CodecSpec Spec { get; }

        public RegistryTable Registry { get; }

        public IReadOnlyList<Session> Sessions { get; }
    }
}
