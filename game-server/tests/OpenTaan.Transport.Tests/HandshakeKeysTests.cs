using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenTaan.Transport;

namespace OpenTaan.Tests.Transport;

/// <summary>
/// Handshake values: never the plaintext seed, and never in a log line. Every value
/// here is invented.
/// </summary>
public sealed class HandshakeKeysTests
{
    // With the second seed and the shift fixed, this first seed makes the two halves
    // cancel the derivation mask exactly, so the derived seed is zero.
    private const ulong DerivesZero = 0x540ADB1180000000;

    [Fact]
    public void RefusesValuesThatDeriveThePlaintextSeed()
    {
        Assert.Equal(OpenTaanCipher.PlaintextSeed, OpenTaanCipher.DeriveSeed(DerivesZero, 0, 1));

        Assert.Throws<ArgumentException>(() => new HandshakeKeys(DerivesZero, 0, 1));
    }

    [Fact]
    public void DerivesTheBaseSeedFromItsValues()
    {
        var keys = new HandshakeKeys(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 3);

        Assert.Equal(OpenTaanCipher.DeriveSeed(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 3), keys.BaseSeed);
    }

    [Fact]
    public void GeneratingDrawsAgainWhenTheValuesDeriveThePlaintextSeed()
    {
        // The first draw derives zero; the second does not.
        using var random = new ScriptedRandom(Draw(DerivesZero, 0, 0), Draw(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 2));

        var keys = HandshakeKeys.Generate(random);

        Assert.Equal(0x0123456789ABCDEFul, keys.Seed1);
        Assert.Equal(3, keys.Shift);
        Assert.NotEqual(OpenTaanCipher.PlaintextSeed, keys.BaseSeed);
    }

    [Fact]
    public void GeneratingFailsLoudlyRatherThanFallingBackToPlaintext()
    {
        // A generator stuck on values that derive zero must not end in a handshake
        // that switches encryption off.
        using var random = new ScriptedRandom(Draw(DerivesZero, 0, 0));

        Assert.Throws<CryptographicException>(() => HandshakeKeys.Generate(random));
    }

    [Fact]
    public void GeneratingDrawsTheShiftAgainRatherThanFavouringSmallOnes()
    {
        // 250 and above would make shifts 1 to 6 more likely than 7 to 10.
        using var random = new ScriptedRandom(
            Draw(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 250),
            Draw(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 249));

        var keys = HandshakeKeys.Generate(random);

        Assert.Equal(HandshakeKeys.MaximumShift, keys.Shift);
    }

    [Fact]
    public void GeneratedShiftsStayInRange()
    {
        using var random = RandomNumberGenerator.Create();

        for (var draw = 0; draw < 1000; draw++)
        {
            var keys = HandshakeKeys.Generate(random);

            Assert.InRange(keys.Shift, HandshakeKeys.MinimumShift, HandshakeKeys.MaximumShift);
            Assert.NotEqual(OpenTaanCipher.PlaintextSeed, keys.BaseSeed);
        }
    }

    [Fact]
    public void PrintingNeverShowsTheSeeds()
    {
        var keys = new HandshakeKeys(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 3);

        var printed = keys.ToString();

        Assert.Contains("Shift = 3", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789ABCDEF", printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(0x0123456789ABCDEFul.ToString(System.Globalization.CultureInfo.InvariantCulture), printed, StringComparison.Ordinal);
    }

    private static byte[] Draw(ulong seed1, ulong seed2, byte shiftByte)
    {
        var bytes = new byte[17];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, seed1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), seed2);
        bytes[16] = shiftByte;
        return bytes;
    }

    /// <summary>Hands out the given draws in order, repeating the last one forever.</summary>
    private sealed class ScriptedRandom(params byte[][] draws) : RandomNumberGenerator
    {
        private int _next;

        public override void GetBytes(byte[] data)
        {
            var draw = draws[Math.Min(_next++, draws.Length - 1)];
            draw.AsSpan(0, data.Length).CopyTo(data);
        }

        public override void GetBytes(Span<byte> data)
        {
            var draw = draws[Math.Min(_next++, draws.Length - 1)];
            draw.AsSpan(0, data.Length).CopyTo(data);
        }
    }
}
