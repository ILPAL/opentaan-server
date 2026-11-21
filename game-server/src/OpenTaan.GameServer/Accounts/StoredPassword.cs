using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenTaan.Protocol;

namespace OpenTaan.GameServer.Accounts;

/// <summary>
/// An account's password as this server keeps it: a salted PBKDF2-HMAC-SHA256 hash of
/// what the client sent, never what it sent. Prints as <c>***</c>.
/// </summary>
/// <remarks>
/// <para>
/// The client never sends a password itself. It sends a digest of it, the same text when
/// the account is created and at every login, and that text is all a login takes. Kept
/// as it arrives, a copy of the accounts would be a list of working logins. Kept as this
/// hash, each guess at one costs <see cref="Iterations"/> rounds, and its own salt makes
/// the guesses at one account worthless against any other.
/// </para>
/// <para>
/// Its text form, <see cref="Format"/>, names the scheme and the rounds it was made with,
/// so raising the rounds for new hashes never stops an older one from matching. Only that
/// exact text is read back: any other, a digest that was never hashed among it, is
/// refused rather than compared.
/// </para>
/// </remarks>
public sealed class StoredPassword
{
    /// <summary>The name its text form starts with.</summary>
    public const string Scheme = "pbkdf2-sha256";

    /// <summary>Bytes of salt, drawn at random for each hash.</summary>
    public const int SaltSize = 16;

    /// <summary>Bytes of hash: one SHA-256 output.</summary>
    public const int HashSize = 32;

    private const char Separator = '$';

    private readonly byte[] _salt;
    private readonly byte[] _hash;

    private StoredPassword(int iterations, byte[] salt, byte[] hash)
    {
        Iterations = iterations;
        _salt = salt;
        _hash = hash;
    }

    /// <summary>How many PBKDF2 rounds it was made with, which every check against it takes too.</summary>
    public int Iterations { get; }

    /// <summary>
    /// Reads text that <see cref="Format"/> wrote. Any other text is refused, including text
    /// that differs only in a way <see cref="Format"/> never writes, such as a leading zero.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out StoredPassword? password)
    {
        password = null;
        if (text?.Split(Separator) is not [Scheme, var rounds, var salt, var hash]
            || !int.TryParse(rounds, NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        var saltBytes = new byte[SaltSize];
        var hashBytes = new byte[HashSize];
        if (!Convert.TryFromBase64String(salt, saltBytes, out var saltWritten) || saltWritten != SaltSize
            || !Convert.TryFromBase64String(hash, hashBytes, out var hashWritten) || hashWritten != HashSize)
        {
            return false;
        }

        var read = new StoredPassword(iterations, saltBytes, hashBytes);
        if (!string.Equals(read.Format(), text, StringComparison.Ordinal))
        {
            return false;
        }

        password = read;
        return true;
    }

    /// <summary>Its text form: <c>pbkdf2-sha256$rounds$salt$hash</c>, with the salt and the hash in base64.</summary>
    public string Format() => string.Join(
        Separator,
        Scheme,
        Iterations.ToString(CultureInfo.InvariantCulture),
        Convert.ToBase64String(_salt),
        Convert.ToBase64String(_hash));

    /// <inheritdoc/>
    public override string ToString() => "***";

    /// <summary>A new hash of <paramref name="digest"/>, which takes <paramref name="iterations"/> rounds.</summary>
    internal static StoredPassword Derive(Secret digest, byte[] salt, int iterations)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentOutOfRangeException.ThrowIfNotEqual(salt.Length, SaltSize, nameof(salt));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        return new StoredPassword(iterations, [.. salt], HashOf(digest, salt, iterations));
    }

    /// <summary>
    /// The PBKDF2-HMAC-SHA256 of <paramref name="digest"/>'s UTF-8 bytes: <see cref="HashSize"/>
    /// bytes, the first block of the function.
    /// </summary>
    internal static byte[] HashOf(Secret digest, ReadOnlySpan<byte> salt, int iterations)
    {
        var bytes = Encoding.UTF8.GetBytes(digest.Reveal());
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Whether <paramref name="digest"/> is what this was made from. Takes
    /// <see cref="Iterations"/> rounds, and compares the hashes in constant time.
    /// </summary>
    internal bool Matches(Secret digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        return CryptographicOperations.FixedTimeEquals(HashOf(digest, _salt, Iterations), _hash);
    }
}
