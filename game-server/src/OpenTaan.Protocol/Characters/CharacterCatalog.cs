namespace OpenTaan.Protocol.Characters;

/// <summary>
/// An account's characters, as <see cref="Accounts.PrUserLoginSucceeded"/> carries them:
/// a list of character descriptions and a list of their appearances.
/// </summary>
/// <remarks>
/// Only the empty catalog is supported so far, which is what an account with no
/// characters gets. The two lists arrive with the character packets. Until then a
/// catalog that lists anything is refused as <see cref="PacketError.Unsupported"/>
/// rather than skipped, because an element's size cannot be known without reading it.
/// </remarks>
[WireClass("CharacterCatalog")]
public sealed record CharacterCatalog
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<CharacterCatalog>.RegistryId;

    /// <summary>The fewest bytes it can occupy: its id and two empty lists.</summary>
    internal const int MinimumSize = 3 * sizeof(uint);

    internal static CharacterCatalog ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var characters = reader.ReadUInt32();
        var appearances = reader.ReadUInt32();
        if (characters != 0 || appearances != 0)
        {
            reader.Fail(PacketError.Unsupported);
        }

        return new CharacterCatalog();
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
    }
}
