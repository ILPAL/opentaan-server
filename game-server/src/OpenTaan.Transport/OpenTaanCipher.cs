using System.Buffers.Binary;

namespace OpenTaan.Transport;

/// <summary>
/// The game protocol's packet cipher: a repeating 16-byte XOR key derived from a
/// 32-bit seed, with the body length and a checksum carried in masked words either
/// side of the body.
/// </summary>
/// <remarks>
/// The cipher belongs to the client, so this server cannot change or strengthen it.
/// The threat model records that as accepted (T-G4): a party who can read the
/// traffic can read the protocol, and nothing is sent to a client that it may not
/// already see.
/// </remarks>
public static class OpenTaanCipher
{
    // The 16-byte key is these four words, each XORed with the session seed.
    private const uint KeyMask0 = 0x14B307C8;
    private const uint KeyMask1 = 0x8CBF12AC;
    private const uint KeyMask2 = 0x240397C1;
    private const uint KeyMask3 = 0xF3BD29C0;

    private const uint SeedDerivationMask = 0xA815B623;
    private const uint HeaderMask = 0xF834A608;
    private const uint ChecksumMask = 0xC9F84A90;

    /// <summary>Bytes in the XOR key.</summary>
    public const int KeyLength = 16;

    /// <summary>Added to a direction's seed after every frame that direction carries.</summary>
    public const uint SeedIncrement = 0x01473F19;

    /// <summary>The seed value reserved to mean "this direction is not encrypted".</summary>
    public const uint PlaintextSeed = 0;

    /// <summary>
    /// Derives a session's base seed from the three values the server sends in its
    /// handshake frame.
    /// </summary>
    /// <remarks>
    /// The shift is applied to 64-bit registers, so only its low six bits matter:
    /// the client's <c>shl r64, cl</c> masks the count, and a 64-bit shift in C# masks
    /// it the same way. A shift of 64 or more therefore behaves as that value modulo
    /// 64 rather than shifting everything away.
    ///
    /// A handshake whose values derive to <see cref="PlaintextSeed"/> must not be sent:
    /// zero would tell the client the connection is unencrypted. Whoever builds the
    /// handshake frame re-rolls in that case.
    /// </remarks>
    public static uint DeriveSeed(ulong seed1, ulong seed2, byte shift)
    {
        var first = (uint)((seed1 << shift) >> 32);
        var second = (uint)((seed2 << shift) >> 32);
        return first ^ second ^ SeedDerivationMask;
    }

    /// <summary>Advances one direction's seed by a single frame.</summary>
    /// <remarks>
    /// Zero means "unencrypted", so an advance that lands there is forced to one.
    /// That correction is why a seed must be carried forward rather than recomputed
    /// from a frame counter: the two agree until the first wrap onto zero and never
    /// again afterwards.
    /// </remarks>
    public static uint AdvanceSeed(uint seed)
    {
        var advanced = seed + SeedIncrement;
        return advanced == PlaintextSeed ? 1u : advanced;
    }

    /// <summary>Writes the 16-byte key for <paramref name="seed"/>.</summary>
    public static void DeriveKey(uint seed, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, KeyLength);

        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), seed ^ KeyMask0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), seed ^ KeyMask1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), seed ^ KeyMask2);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), seed ^ KeyMask3);
    }

    /// <summary>Encrypts a body and returns the checksum that belongs in its trailer.</summary>
    public static uint Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> destination, uint seed)
    {
        Apply(plaintext, destination, seed);
        return Checksum(plaintext);
    }

    /// <summary>Decrypts a body and returns the checksum its trailer should carry.</summary>
    /// <remarks>
    /// Both directions checksum the <em>plaintext</em>. The transformation is its own
    /// inverse, so it is tempting to checksum whatever was fed in; doing that makes
    /// the two directions disagree, because encryption is fed plaintext and decryption
    /// is fed ciphertext. The two only coincide when the body is an even number of
    /// whole key blocks, which is why such a mistake survives casual testing.
    /// </remarks>
    public static uint Decrypt(ReadOnlySpan<byte> ciphertext, Span<byte> destination, uint seed)
    {
        Apply(ciphertext, destination, seed);
        return Checksum(destination.Slice(0, ciphertext.Length));
    }

    /// <summary>Builds the header word for a body of <paramref name="bodyLength"/> bytes.</summary>
    public static uint EncodeHeader(uint seed, int bodyLength) =>
        seed ^ (uint)(bodyLength + TransportLimits.ChecksumSize) ^ HeaderMask;

    /// <summary>
    /// Recovers a body length from a header word, refusing any length that no
    /// legitimate peer could have framed.
    /// </summary>
    /// <remarks>
    /// The declared length is chosen by whoever sent the frame, so it is checked
    /// before a single byte is reserved for it (threat model T-G1).
    /// </remarks>
    public static bool TryDecodeBodyLength(uint header, uint seed, out int bodyLength)
    {
        var declared = header ^ seed ^ HeaderMask;

        // The declared value covers the body and its trailing checksum.
        const uint Smallest = TransportLimits.ChecksumSize;
        const uint Largest = TransportLimits.MaxEncryptedBody + TransportLimits.ChecksumSize;

        if (declared < Smallest || declared > Largest)
        {
            bodyLength = 0;
            return false;
        }

        bodyLength = (int)(declared - TransportLimits.ChecksumSize);
        return true;
    }

    /// <summary>Masks a checksum for the wire, and recovers one from it.</summary>
    public static uint MaskChecksum(uint seed, uint checksum) => seed ^ checksum ^ ChecksumMask;

    private static void Apply(ReadOnlySpan<byte> source, Span<byte> destination, uint seed)
    {
        Span<byte> key = stackalloc byte[KeyLength];
        DeriveKey(seed, key);

        // The key repeats from the start of the body, and the trailing partial block
        // restarts it rather than continuing where the last whole block left off.
        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = (byte)(source[i] ^ key[i % KeyLength]);
        }
    }

    private static uint Checksum(ReadOnlySpan<byte> plaintext)
    {
        uint checksum = 0;

        var blocks = plaintext.Length / KeyLength;
        for (var block = 0; block < blocks; block++)
        {
            var span = plaintext.Slice(block * KeyLength, KeyLength);
            for (var word = 0; word < KeyLength / 4; word++)
            {
                checksum ^= BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(word * 4, 4));
            }
        }

        // Trailing bytes fold in one at a time at rising bit offsets. The remainder is
        // always shorter than the key, so the offset never reaches the word width.
        for (var i = blocks * KeyLength; i < plaintext.Length; i++)
        {
            checksum ^= (uint)plaintext[i] << (i - blocks * KeyLength);
        }

        return checksum;
    }
}
