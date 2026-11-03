using System.Text;

namespace OpenTaan.Protocol;

/// <summary>How the client turns a class name into the registry id that leads its bytes.</summary>
/// <remarks>
/// The <see cref="ZeroStartAdler32"/> checksum of the ASCII name. Because the id is
/// computed from the name, renaming a wire class is a protocol change: the client would
/// no longer recognise it.
/// </remarks>
public static class RegistryIds
{
    /// <summary>The registry id of the class the client registers as <paramref name="wireName"/>.</summary>
    /// <exception cref="ArgumentException">The name is empty or not ASCII.</exception>
    public static uint Of(string wireName)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireName);

        if (!Ascii.IsValid(wireName))
        {
            throw new ArgumentException("A wire name is ASCII.", nameof(wireName));
        }

        return ZeroStartAdler32.Of(Encoding.ASCII.GetBytes(wireName));
    }
}
