using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The room a player is now in: the answer to a request to create, enter or quick-join one.</summary>
/// <remarks>
/// <para>
/// The client keeps the room only if it shows none already, and keeps the slots as they
/// are listed. Later changes find a slot in that list by its index, and some find it by
/// its place in the list without checking it, so every one of the room's
/// <see cref="GameSlotInfo.SlotCount"/> slots is listed, in order: a reply listing them
/// any other way can be neither read nor written.
/// </para>
/// <para>
/// The two team names are read only in arena channels, where they name the two teams;
/// this server sends them empty until it has arena rooms.
/// </para>
/// </remarks>
/// <param name="Room">The room.</param>
/// <param name="Option">Who hosts it.</param>
/// <param name="Slots">Every slot of the room, in order.</param>
/// <param name="Difficulty">
/// The mission's difficulty, which the client reads only in a mission room: 0, 1 or 2.
/// Elsewhere it reads nothing here; the reference sessions carry the player's first slot.
/// </param>
/// <param name="TeamOneName">The first team's name, read only in arena channels.</param>
/// <param name="TeamTwoName">The second team's name, read only in arena channels.</param>
[WireClass("PcGameSession_Init")]
public sealed record PcGameSessionInit(
    GameSessionInfo Room,
    GameSessionOption Option,
    IReadOnlyList<GameSlotInfo> Slots,
    byte Difficulty,
    string TeamOneName,
    string TeamTwoName) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionInit>.RegistryId;

    /// <summary>The room.</summary>
    public GameSessionInfo Room { get; } = Room ?? throw new ArgumentNullException(nameof(Room));

    /// <summary>Who hosts it.</summary>
    public GameSessionOption Option { get; } = Option ?? throw new ArgumentNullException(nameof(Option));

    /// <summary>Every slot of the room, in order.</summary>
    public IReadOnlyList<GameSlotInfo> Slots { get; } = Slots ?? throw new ArgumentNullException(nameof(Slots));

    /// <summary>The first team's name, read only in arena channels.</summary>
    public string TeamOneName { get; } = TeamOneName ?? throw new ArgumentNullException(nameof(TeamOneName));

    /// <summary>The second team's name, read only in arena channels.</summary>
    public string TeamTwoName { get; } = TeamTwoName ?? throw new ArgumentNullException(nameof(TeamTwoName));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionInit> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var room = GameSessionInfo.ReadFrom(ref reader);
        var option = GameSessionOption.ReadFrom(ref reader);
        var slots = reader.ReadList(GameSlotInfo.MinimumSize, GameSlotInfo.ReadFrom);
        var difficulty = reader.ReadByte();
        var teamOneName = reader.ReadUtf16String();
        var teamTwoName = reader.ReadUtf16String();
        if (!ListsEverySlotInOrder(slots))
        {
            reader.Fail(PacketError.UnexpectedValue);
        }

        return reader.Finish(new PcGameSessionInit(room, option, slots, difficulty, teamOneName, teamTwoName));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Slots"/> does not list every slot of a room, in order.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        if (!ListsEverySlotInOrder(Slots))
        {
            throw new InvalidOperationException($"A room is sent with all of its {GameSlotInfo.SlotCount} slots, in order.");
        }

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        Room.WriteTo(writer);
        Option.WriteTo(writer);
        writer.WriteList(Slots, static (list, slot) => slot.WriteTo(list));
        writer.WriteByte(Difficulty);
        writer.WriteUtf16String(TeamOneName);
        writer.WriteUtf16String(TeamTwoName);
    }

    private static bool ListsEverySlotInOrder(IReadOnlyList<GameSlotInfo> slots) =>
        slots.Count == GameSlotInfo.SlotCount && slots.Select((slot, index) => slot.SlotIndex == index).All(inPlace => inPlace);
}
