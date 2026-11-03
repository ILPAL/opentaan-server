using OpenTaan.Protocol;
using OpenTaan.Protocol.Data;

namespace OpenTaan.Tests.Protocol;

/// <summary>The client's binary data tree.</summary>
public sealed class DataNodeTests
{
    [Fact]
    public void ANodeIsItsTagTextAttributesAndChildrenInThatOrder()
    {
        var node = new DataNode("srv", "t", [new NamedValue("id", "7")], [DataNode.Element("ch", [], [])]);

        byte[] expected =
        [
            .. Encoded.Utf16("srv"),
            .. Encoded.Utf16("t"),
            .. Encoded.UInt32(1), .. Encoded.Utf16("id"), .. Encoded.Utf16("7"),
            .. Encoded.UInt32(1),
            .. Encoded.Utf16("ch"), .. Encoded.Utf16(string.Empty), .. Encoded.UInt32(0), .. Encoded.UInt32(0),
        ];

        Assert.Equal(expected, node.ToBytes());

        Assert.Equal(PacketError.None, DataNode.TryRead(expected, out var read));
        Assert.Equal(node.ToBytes(), read.ToBytes());
        Assert.Equal("7", read.Attribute("id"));
        Assert.Null(read.Attribute("missing"));
    }

    [Fact]
    public void ATreeNestedAsDeepAsAllowedIsRead()
    {
        Assert.Equal(PacketError.None, DataNode.TryRead(Nested(DataNode.MaximumDepth).ToBytes(), out _));
    }

    [Fact]
    public void ATreeNestedDeeperThanAllowedIsRefused()
    {
        Assert.Equal(PacketError.UnexpectedValue, DataNode.TryRead(Nested(DataNode.MaximumDepth + 1).ToBytes(), out var node));
        Assert.Empty(node.Children);
    }

    [Fact]
    public void AChildCountLargerThanTheBytesLeftIsRefused()
    {
        byte[] bytes = [.. Encoded.Utf16(string.Empty), .. Encoded.Utf16(string.Empty), .. Encoded.UInt32(0), .. Encoded.UInt32(uint.MaxValue)];

        Assert.Equal(PacketError.CountOutOfRange, DataNode.TryRead(bytes, out _));
    }

    [Fact]
    public void BytesAfterTheTreeAreRefused()
    {
        byte[] bytes = [.. DataNode.Element("ch", [], []).ToBytes(), 0x00];

        Assert.Equal(PacketError.TrailingBytes, DataNode.TryRead(bytes, out _));
    }

    private static DataNode Nested(int depth) =>
        depth == 1 ? DataNode.Element("leaf", [], []) : DataNode.Element("level", [], [Nested(depth - 1)]);
}
