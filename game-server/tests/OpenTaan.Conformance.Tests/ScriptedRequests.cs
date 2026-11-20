using System.Buffers.Binary;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.Conformance;

/// <summary>Recorded requests that carry bytes no client writes, and the requests a client sends in their place.</summary>
/// <remarks>
/// The reference sessions were scripted, and their request for a room's captains to be
/// chosen carries one byte after its id. The client writes the id alone, and the codec
/// refuses anything more, as a server should refuse what no client sends. So that request
/// is replayed as the client sends it, and checked to be refused as recorded.
/// </remarks>
internal static class ScriptedRequests
{
    /// <summary>Whether <paramref name="recorded"/> carries bytes the client never writes.</summary>
    public static bool IsScripted(byte[] recorded) =>
        recorded.Length > sizeof(uint)
        && BinaryPrimitives.ReadUInt32LittleEndian(recorded) == PqGameSessionChiefDecision.RegistryId;

    /// <summary><paramref name="recorded"/> as the client sends it: unchanged, unless it carries bytes the client never writes.</summary>
    public static byte[] AsTheClientSendsIt(byte[] recorded) =>
        IsScripted(recorded) ? recorded[..sizeof(uint)] : recorded;
}
