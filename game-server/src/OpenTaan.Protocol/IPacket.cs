using System.Buffers;

namespace OpenTaan.Protocol;

/// <summary>A packet whose body can be written: its registry id followed by its fields.</summary>
public interface IPacket
{
    /// <summary>Writes the body.</summary>
    void Write(IBufferWriter<byte> destination);
}
