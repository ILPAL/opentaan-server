namespace OpenTaan.Protocol;

/// <summary>How the client turns a class name into the registry id that leads its bytes.</summary>
/// <remarks>
/// Adler-32 of the ASCII name, except that the running sum starts at 0 where standard
/// Adler-32 starts at 1, so a standard library implementation gives the wrong id.
/// Because the id is computed from the name, renaming a wire class is a protocol
/// change: the client would no longer recognise it.
/// </remarks>
public static class RegistryIds
{
    private const uint Modulus = 65521;

    /// <summary>The registry id of the class the client registers as <paramref name="wireName"/>.</summary>
    /// <exception cref="ArgumentException">The name is empty or not ASCII.</exception>
    public static uint Of(string wireName)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireName);

        uint sum = 0;
        uint sumOfSums = 0;
        foreach (var character in wireName)
        {
            if (character > '\u007F')
            {
                throw new ArgumentException("A wire name is ASCII.", nameof(wireName));
            }

            sum = (sum + character) % Modulus;
            sumOfSums = (sumOfSums + sum) % Modulus;
        }

        return (sumOfSums << 16) | sum;
    }
}
