namespace OpenTaan.Protocol;

/// <summary>
/// The name the client registers a class under. Its registry id is computed from it;
/// see <see cref="WireIdentity{T}"/>.
/// </summary>
/// <remarks>
/// The wire name can differ from the C# name: the client's names may contain
/// underscores, which C# names here do not.
/// </remarks>
/// <param name="name">The client's name for the class, exactly as it registers it.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class WireClassAttribute(string name) : Attribute
{
    /// <summary>The client's name for the class.</summary>
    public string Name { get; } = name;
}
