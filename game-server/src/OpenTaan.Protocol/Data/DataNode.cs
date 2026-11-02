namespace OpenTaan.Protocol.Data;

/// <summary>
/// One node of the client's binary data tree, which is shaped like an XML element: a
/// tag, text, named attributes and child nodes. The client keeps its channel list, among
/// other things, in this form, wrapped in a <see cref="DataEnvelope"/>.
/// </summary>
/// <remarks>
/// On the wire: the tag, the text, a 32-bit count of attributes each written as its name
/// and value, then a 32-bit count of children each written the same way as this node.
/// Every string is UTF-16 with a 32-bit count of code units. A tree nested deeper than
/// <see cref="MaximumDepth"/> is refused, so a crafted one cannot exhaust the stack.
/// </remarks>
/// <param name="Tag">The node's tag. The root of a tree has an empty one.</param>
/// <param name="Text">The node's text, usually empty.</param>
/// <param name="Attributes">The node's attributes, in order.</param>
/// <param name="Children">The node's children, in order.</param>
public sealed record DataNode(string Tag, string Text, IReadOnlyList<NamedValue> Attributes, IReadOnlyList<DataNode> Children)
{
    /// <summary>How deep a tree may nest, the root counting as the first level.</summary>
    public const int MaximumDepth = 32;

    // A node with empty strings and no attributes or children: four 32-bit counts.
    private const int MinimumSize = 4 * sizeof(uint);

    /// <summary>A node with a tag, attributes and children, and no text.</summary>
    public static DataNode Element(string tag, IReadOnlyList<NamedValue> attributes, IReadOnlyList<DataNode> children) =>
        new(tag, string.Empty, attributes, children);

    /// <summary>The value of the first attribute named <paramref name="name"/>, or null.</summary>
    public string? Attribute(string name) =>
        Attributes.FirstOrDefault(attribute => attribute.Name == name)?.Value;

    /// <summary>Reads a tree from the whole of <paramref name="bytes"/>; anything left over is refused.</summary>
    public static PacketError TryRead(ReadOnlySpan<byte> bytes, out DataNode node)
    {
        var reader = new PacketReader(bytes);
        node = ReadFrom(ref reader, depth: 1);
        var error = reader.Error != PacketError.None ? reader.Error
            : reader.Remaining != 0 ? PacketError.TrailingBytes
            : PacketError.None;
        node = error == PacketError.None ? node : Empty;
        return error;
    }

    /// <summary>The tree's bytes.</summary>
    public byte[] ToBytes()
    {
        var destination = new System.Buffers.ArrayBufferWriter<byte>();
        WriteTo(new PacketWriter(destination));
        return destination.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads a tree from a <see cref="DataEnvelope"/> preceded by its 32-bit length,
    /// refusing a tree that unwraps to more than <paramref name="maximumSize"/> bytes.
    /// </summary>
    internal static DataNode ReadEnveloped(ref PacketReader reader, int maximumSize)
    {
        var envelope = reader.ReadCountedBytes();
        if (reader.Error != PacketError.None)
        {
            return Empty;
        }

        var error = DataEnvelope.TryUnwrap(envelope, maximumSize, out var tree);
        var node = Empty;
        if (error == PacketError.None)
        {
            error = TryRead(tree, out node);
        }

        if (error != PacketError.None)
        {
            reader.Fail(error);
        }

        return node;
    }

    /// <summary>Writes the tree in an uncompressed <see cref="DataEnvelope"/> preceded by its 32-bit length.</summary>
    internal void WriteEnveloped(PacketWriter writer) => writer.WriteCountedBytes(DataEnvelope.Wrap(ToBytes()));

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUtf16String(Tag);
        writer.WriteUtf16String(Text);
        writer.WriteList(Attributes, static (list, attribute) =>
        {
            list.WriteUtf16String(attribute.Name);
            list.WriteUtf16String(attribute.Value);
        });
        writer.WriteList(Children, static (list, child) => child.WriteTo(list));
    }

    private static DataNode Empty { get; } = new(string.Empty, string.Empty, [], []);

    private static DataNode ReadFrom(ref PacketReader reader, int depth)
    {
        if (depth > MaximumDepth)
        {
            reader.Fail(PacketError.UnexpectedValue);
            return Empty;
        }

        var tag = reader.ReadUtf16String();
        var text = reader.ReadUtf16String();
        var attributes = reader.ReadList(
            2 * sizeof(uint),
            static (ref PacketReader attribute) => new NamedValue(attribute.ReadUtf16String(), attribute.ReadUtf16String()));
        var children = reader.ReadList(MinimumSize, (ref PacketReader child) => ReadFrom(ref child, depth + 1));
        return new DataNode(tag, text, attributes, children);
    }
}

/// <summary>A named value on a <see cref="DataNode"/>.</summary>
/// <param name="Name">The attribute's name.</param>
/// <param name="Value">The attribute's value, always text.</param>
public sealed record NamedValue(string Name, string Value);
