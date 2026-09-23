using System.Reflection;

namespace OpenTaan.Protocol;

/// <summary>
/// A wire class's name and registry id, read from its <see cref="WireClassAttribute"/>
/// once per type and kept.
/// </summary>
/// <typeparam name="T">A class marked with <see cref="WireClassAttribute"/>.</typeparam>
/// <remarks>
/// A type without the attribute fails on first use, with a
/// <see cref="TypeInitializationException"/> naming it; a test checks every wire class
/// carries one, so that never happens at run time.
/// </remarks>
public static class WireIdentity<T>
    where T : class
{
    /// <summary>The client's name for the class.</summary>
    public static string Name { get; } = typeof(T).GetCustomAttribute<WireClassAttribute>()?.Name
        ?? throw new InvalidOperationException($"{typeof(T).Name} has no {nameof(WireClassAttribute)}.");

    /// <summary>The id that leads the class's bytes.</summary>
    public static uint RegistryId { get; } = RegistryIds.Of(Name);
}
