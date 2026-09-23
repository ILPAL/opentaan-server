using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenTaan.Transport;

/// <summary>
/// The three values a connection's handshake frame carries, and the base seed both
/// ends derive from them.
/// </summary>
/// <remarks>
/// The values travel in the clear, so they do not make the traffic confidential; the
/// cipher belongs to the client and cannot (threat model T-G4). Two properties still
/// matter, and both are guaranteed here rather than left to the caller:
///
/// <list type="bullet">
/// <item>
/// The derived seed is never <see cref="OpenTaanCipher.PlaintextSeed"/>. Zero tells
/// the client the connection is unencrypted, so a handshake that derives it would
/// switch encryption off for the whole session. Such values cannot be constructed.
/// </item>
/// <item>
/// Generated values come from a cryptographic random number generator. A general
/// purpose generator can be predicted from its past output, which would let one
/// client work out the seeds of connections it never saw.
/// </item>
/// </list>
/// </remarks>
public sealed class HandshakeKeys
{
    /// <summary>The smallest shift <see cref="Generate"/> picks.</summary>
    public const byte MinimumShift = 1;

    /// <summary>The largest shift <see cref="Generate"/> picks.</summary>
    public const byte MaximumShift = 10;

    // A healthy generator almost never needs a second attempt; running out means it
    // is broken, and failing loudly is the only safe answer.
    private const int MaximumAttempts = 64;

    private const int ShiftRange = MaximumShift - MinimumShift + 1;

    // The largest multiple of the range that fits in a byte. Bytes at or above it are
    // drawn again, so every shift is equally likely.
    private const int UnbiasedShiftLimit = byte.MaxValue + 1 - ((byte.MaxValue + 1) % ShiftRange);

    /// <summary>Takes values chosen elsewhere, such as a recorded session's.</summary>
    /// <exception cref="ArgumentException">The values derive the seed reserved for plaintext.</exception>
    public HandshakeKeys(ulong seed1, ulong seed2, byte shift)
    {
        var baseSeed = OpenTaanCipher.DeriveSeed(seed1, seed2, shift);
        if (baseSeed == OpenTaanCipher.PlaintextSeed)
        {
            throw new ArgumentException(
                "These values derive the seed reserved for plaintext, so they would switch encryption off.");
        }

        Seed1 = seed1;
        Seed2 = seed2;
        Shift = shift;
        BaseSeed = baseSeed;
    }

    /// <summary>The first derivation value.</summary>
    public ulong Seed1 { get; }

    /// <summary>The second derivation value.</summary>
    public ulong Seed2 { get; }

    /// <summary>How far both values are shifted before they are combined.</summary>
    public byte Shift { get; }

    /// <summary>The seed both directions start from. Never <see cref="OpenTaanCipher.PlaintextSeed"/>.</summary>
    public uint BaseSeed { get; }

    /// <summary>Picks new values for a connection.</summary>
    /// <param name="random">
    /// The source of randomness: <see cref="RandomNumberGenerator.Create()"/> in the
    /// server, a fixed sequence in tests.
    /// </param>
    /// <exception cref="CryptographicException">
    /// The generator kept producing values that cannot be used, so it is not working.
    /// </exception>
    public static HandshakeKeys Generate(RandomNumberGenerator random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Span<byte> bytes = stackalloc byte[(2 * sizeof(ulong)) + sizeof(byte)];

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            random.GetBytes(bytes);

            var shiftByte = bytes[2 * sizeof(ulong)];
            if (shiftByte >= UnbiasedShiftLimit)
            {
                continue;
            }

            var seed1 = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            var seed2 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[sizeof(ulong)..]);
            var shift = (byte)(MinimumShift + (shiftByte % ShiftRange));

            if (OpenTaanCipher.DeriveSeed(seed1, seed2, shift) != OpenTaanCipher.PlaintextSeed)
            {
                return new HandshakeKeys(seed1, seed2, shift);
            }
        }

        throw new CryptographicException(
            $"No usable handshake values in {MaximumAttempts} attempts; the random number generator is not working.");
    }

    /// <summary>Names the shift, never the seeds.</summary>
    public override string ToString() => $"{nameof(HandshakeKeys)} {{ Shift = {Shift} }}";
}
