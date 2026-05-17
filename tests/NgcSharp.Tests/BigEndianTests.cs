using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class BigEndianTests
{
    [Fact]
    public void ReadsAndWritesUInt32InBigEndianOrder()
    {
        Span<byte> buffer = stackalloc byte[4];

        BigEndian.WriteUInt32(buffer, 0x1234_ABCD);

        Assert.Equal([0x12, 0x34, 0xAB, 0xCD], buffer.ToArray());
        Assert.Equal(0x1234_ABCDu, BigEndian.ReadUInt32(buffer));
    }
}
