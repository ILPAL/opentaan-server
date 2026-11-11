using System.Buffers;

namespace OpenTaan.Protocol.Results;

/// <summary>A match is over: sent to everyone still in it, with how it ended.</summary>
/// <remarks>
/// <para>
/// The client builds its result screen from this, with the rewards it was sent just
/// before in <see cref="PcResultSetReward"/>: rewards that arrive after it leave the
/// screen's cards empty. It marks every ready character in the room as no longer ready,
/// adds a win or a loss and any medals to each of its own characters, and leaves the
/// result screen for the room by itself about nineteen seconds later.
/// </para>
/// <para>
/// Nothing about the match's turns may follow it: the client would act on it and leave its
/// result screen.
/// </para>
/// </remarks>
/// <param name="Result">How the match ended.</param>
/// <param name="EndGameMode">A value the client keeps with the result; the reference sessions carry 0.</param>
[WireClass("PcGameSession_EndGame")]
public sealed record PcGameSessionEndGame(GameResult Result, byte EndGameMode) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionEndGame>.RegistryId;

    /// <summary>How the match ended.</summary>
    public GameResult Result { get; } = Result ?? throw new ArgumentNullException(nameof(Result));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionEndGame> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var result = GameResult.ReadFrom(ref reader);
        var endGameMode = reader.ReadByte();

        // Null only when the read has failed, which Finish reports.
        return result is null
            ? new PacketReadResult<PcGameSessionEndGame>(null, reader.Error)
            : reader.Finish(new PcGameSessionEndGame(result, endGameMode));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        Result.WriteTo(writer);
        writer.WriteByte(EndGameMode);
    }
}
