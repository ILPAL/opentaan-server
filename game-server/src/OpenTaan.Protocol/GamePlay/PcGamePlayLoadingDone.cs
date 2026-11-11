using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A client has loaded a match: it reports where each of its own characters stands.</summary>
/// <remarks>
/// <para>
/// The client sends it once for each match, once its opening view of the field has gone
/// round every character, and expects no answer. It lists only its own characters, so at
/// most <see cref="MaxCharacters"/>: one for each account a connection plays. A body
/// listing more comes from no real client, and is refused.
/// </para>
/// <para>
/// In a mission the client then asks for the mission's first level at once.
/// </para>
/// </remarks>
/// <param name="Characters">Where each of the client's own characters stands, by slot.</param>
[WireClass("PcGamePlay_LoadingDone")]
public sealed record PcGamePlayLoadingDone(IReadOnlyList<LoadedCharacter> Characters) : IPacket
{
    /// <summary>The most characters a report lists: one for each account a connection plays.</summary>
    public const int MaxCharacters = 2;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayLoadingDone>.RegistryId;

    /// <summary>Where each of the client's own characters stands, by slot.</summary>
    public IReadOnlyList<LoadedCharacter> Characters { get; } = (Characters ?? throw new ArgumentNullException(nameof(Characters))).Count <= MaxCharacters
        ? Characters
        : throw new ArgumentException($"A client reports at most {MaxCharacters} characters.", nameof(Characters));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayLoadingDone> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);

        // The count is a single byte here, not the usual 32 bits.
        var count = reader.ReadByte();
        if (count > MaxCharacters)
        {
            reader.Fail(PacketError.CountOutOfRange);
            count = 0;
        }

        var characters = new LoadedCharacter[count];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = LoadedCharacter.ReadFrom(ref reader);
        }

        return reader.Finish(new PcGamePlayLoadingDone(characters));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Characters.Count);
        foreach (var character in Characters)
        {
            character.WriteTo(writer);
        }
    }

    /// <summary>Whether both report the same characters at the same places, in the same order.</summary>
    public bool Equals(PcGamePlayLoadingDone? other) => other is not null && Characters.SequenceEqual(other.Characters);

    /// <summary>A hash of every entry, consistent with <see cref="Equals(PcGamePlayLoadingDone)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var character in Characters)
        {
            hash.Add(character);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Where one of a client's own characters stands once the match has loaded.</summary>
/// <param name="SlotIndex">The character's slot, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the character stands, across the map.</param>
/// <param name="Y">Where the character stands, down the map.</param>
public sealed record LoadedCharacter(byte SlotIndex, int X, int Y)
{
    /// <summary>The character's slot, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    internal static LoadedCharacter ReadFrom(ref PacketReader reader)
    {
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return new LoadedCharacter(slotIndex, x, y);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteByte(SlotIndex);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
    }
}
