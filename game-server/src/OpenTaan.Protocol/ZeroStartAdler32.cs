namespace OpenTaan.Protocol;

/// <summary>
/// The checksum the client computes everywhere it uses Adler-32: standard Adler-32,
/// except that the running sum starts at 0 where the standard starts at 1, so a library
/// implementation gives a different value.
/// </summary>
/// <remarks>
/// The client names its classes with it (<see cref="RegistryIds"/>) and checks the
/// contents of its data envelopes with it (<see cref="Data.DataEnvelope"/>).
/// </remarks>
public static class ZeroStartAdler32
{
    private const uint Modulus = 65521;

    /// <summary>The checksum of <paramref name="bytes"/>.</summary>
    public static uint Of(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        uint sumOfSums = 0;
        foreach (var value in bytes)
        {
            sum = (sum + value) % Modulus;
            sumOfSums = (sumOfSums + sum) % Modulus;
        }

        return (sumOfSums << 16) | sum;
    }
}
