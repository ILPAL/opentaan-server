using System.Text.Json.Serialization;

namespace OpenTaan.Tests.Conformance;

/// <summary>The packet layouts, as the contract declares them.</summary>
public sealed record CodecSpec(int SpecVersion, IReadOnlyList<ClassSpec> Classes);

/// <summary>
/// One packet class. <see cref="FixedSize"/> is null when the layout is variable,
/// and <see cref="Fields"/> is empty for a class the contract does not describe.
/// </summary>
public sealed record ClassSpec(
    string Name,
    string WireName,
    uint RegistryId,
    string Status,
    int? FixedSize,
    IReadOnlyList<FieldSpec> Fields);

/// <summary>One field of a packet class.</summary>
public sealed record FieldSpec(string Type, int? Bytes, string? Name, string Source);

/// <summary>The registry id of every packet class.</summary>
public sealed record RegistryIds(int Count, IReadOnlyDictionary<string, uint> Ids);

/// <summary>One recorded session.</summary>
public sealed record Session(
    string Lifecycle,
    string Clock,
    IReadOnlyList<Exchange> Exchanges)
{
    /// <summary>The file this session was read from, for test failure messages.</summary>
    [JsonIgnore]
    public string Origin { get; init; } = string.Empty;

    /// <summary>The handshake values this session was recorded with, if it has any.</summary>
    [JsonIgnore]
    public CryptoParameters? Handshake => Exchanges.Select(exchange => exchange.Crypto).FirstOrDefault(c => c is not null);
}

/// <summary>One step of a session: what arrived, and what went back.</summary>
public sealed record Exchange(
    string? Event,
    CryptoParameters? Crypto,
    [property: JsonPropertyName("in")] string? Inbound,
    [property: JsonPropertyName("out")] IReadOnlyList<OutboundFrame>? Outbound)
{
    /// <summary>Frames sent in this exchange, never null.</summary>
    [JsonIgnore]
    public IReadOnlyList<OutboundFrame> Frames => Outbound ?? [];
}

/// <summary>The three values the handshake frame carries.</summary>
public sealed record CryptoParameters(string Seed1, string Seed2, byte Shift)
{
    /// <summary>The first seed, as the handshake frame carries it.</summary>
    [JsonIgnore]
    public ulong FirstSeed => ulong.Parse(Seed1, System.Globalization.NumberStyles.HexNumber, null);

    /// <summary>The second seed, as the handshake frame carries it.</summary>
    [JsonIgnore]
    public ulong SecondSeed => ulong.Parse(Seed2, System.Globalization.NumberStyles.HexNumber, null);
}

/// <summary>One frame the server sent, recorded as the plaintext packet body.</summary>
/// <param name="To">Who the frame was addressed to.</param>
/// <param name="Hex">The packet body: a registry id followed by its fields.</param>
/// <param name="Encrypted">
/// Whether the frame travelled over the encrypted channel. The recorded bytes are
/// plaintext either way; this says which framing carried them.
/// </param>
public sealed record OutboundFrame(string? To, string Hex, bool Encrypted)
{
    /// <summary>The recorded body as bytes.</summary>
    public byte[] Body() => Convert.FromHexString(Hex);
}
