namespace OpenTaan.Protocol;

/// <summary>
/// A credential carried by a packet, such as a password hash. It prints as
/// <c>***</c>, so a packet that is logged, traced or shown in a failed assertion
/// never takes its credential with it.
/// </summary>
/// <remarks>
/// A C# record's generated <c>ToString</c> prints every member. Holding the value in
/// this type is what keeps it out of that output; reaching the value takes a
/// deliberate call to <see cref="Reveal"/>.
/// </remarks>
public sealed class Secret : IEquatable<Secret>
{
    private readonly string _value;

    /// <summary>Wraps <paramref name="value"/>.</summary>
    public Secret(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>The credential itself. Never log it.</summary>
    public string Reveal() => _value;

    /// <inheritdoc/>
    public override string ToString() => "***";

    /// <inheritdoc/>
    public bool Equals(Secret? other) => other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Secret);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);
}
