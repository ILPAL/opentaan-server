namespace OpenTaan.GameServer.Characters;

/// <summary>
/// The rules the client applies to a new character's name, applied again here
/// because a modified client need not apply them at all.
/// </summary>
/// <remarks>
/// <para>
/// These are the rules of the client build this server speaks to, whose character set
/// is Latin with Vietnamese. Its name box holds at most <see cref="MaximumLength"/>
/// UTF-16 code units, and every character must be a digit, a character from
/// <c>A</c> to <c>z</c>, or a Latin letter with diacritics. The client tests that middle
/// range as a range, so it also takes in the six marks between <c>Z</c> and <c>a</c>
/// (<c>[ \ ] ^ _ `</c>); this server accepts what the client sends.
/// </para>
/// <para>
/// Every accented letter allowed is a single precomposed character, and combining
/// marks are not allowed, so one name has one spelling and a case-insensitive
/// comparison is enough to keep names unique.
/// </para>
/// </remarks>
public static class CharacterNames
{
    /// <summary>The fewest UTF-16 code units a name can have.</summary>
    public const int MinimumLength = 4;

    /// <summary>The most UTF-16 code units a name can have: what the client's name box holds.</summary>
    public const int MaximumLength = 16;

    /// <summary>How names are compared: by <see cref="KeyOf"/>, so two names are one name exactly when their keys are one.</summary>
    public static StringComparer Comparer { get; } = new KeyComparer();

    /// <summary>
    /// The key a name is kept unique on: the name in capitals, so two names that differ only
    /// in case are one name. Every store compares names by it, the database among them.
    /// </summary>
    /// <remarks>
    /// Worked out here rather than by the database, whose own case rules, under the C
    /// collation it runs with, change the case of ASCII letters only, and would let two
    /// Vietnamese names that differ only in case be two characters. <see cref="Comparer"/>
    /// is built from it rather than from a comparison of its own: .NET's case-insensitive
    /// comparison and its capitals disagree on the long s, ſ, which it takes as another
    /// letter than s and writes as S in capitals.
    /// </remarks>
    public static string KeyOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.ToUpperInvariant();
    }

    /// <summary>Whether <paramref name="name"/> is a name the client would send.</summary>
    public static bool IsValid(string? name) =>
        name is { Length: >= MinimumLength and <= MaximumLength } && name.All(IsAllowed);

    // Compares names by their keys, so it can never disagree with them.
    private sealed class KeyComparer : StringComparer
    {
        public override int Compare(string? x, string? y) =>
            string.CompareOrdinal(x is null ? null : KeyOf(x), y is null ? null : KeyOf(y));

        public override bool Equals(string? x, string? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && string.Equals(KeyOf(x), KeyOf(y), StringComparison.Ordinal));

        public override int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(KeyOf(obj));
    }

    private static bool IsAllowed(char character) => character is
        (>= '0' and <= '9')
        or (>= 'A' and <= 'z')
        or (>= 'À' and <= 'ȶ')
        or (>= 'Ạ' and <= 'ỹ');
}
